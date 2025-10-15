using Microsoft.Extensions.Options;
using Modbus;
using Modbus.Device;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Diagnostics;
using System.Threading; // Interlocked, SemaphoreSlim
using System.Linq;     // For .Where in barcode clean
using System.IO;       // IOException for transport detection

namespace BlazorApp5.Services
{
    /// <summary>
    /// Modbus TCP service hardened for DMV/vision camera use-cases.
    /// - Single connection with queueing (SemaphoreSlim)
    /// - Auto-reconnect on transport errors (one retry)
    /// - Consistent address map (DET6 default), with graceful fallbacks
    /// - Public APIs used by Index.razor kept the same
    /// </summary>
    public class ModbusService : IDisposable
    {
        private const string DefaultIp = "192.168.1.81";
        private const int DefaultPort = 502;
        private const byte DefaultSlaveId = 1;
        private static readonly ushort[] DefaultTriggerAddresses = new ushort[] { 0x0000, 0x0001, 0x0002 };
        private const ushort DefaultPassRegister = 3000;
        private const byte DefaultPassBit = 0;
        private const ushort DefaultFailRegister = 3001;
        private const byte DefaultFailBit = 0;
        private const ushort DefaultQrRegister = 5000;
        private const int DefaultQrLength = 40;
        private const ushort DefaultWorkOrderReadyRegister = 100;
        private const ushort DefaultWorkOrderMissingRegister = 103;
        private const byte DefaultWorkOrderMissingBit = 0;
        private const ushort DefaultManualTriggerRegister = 102;
        private const byte DefaultManualTriggerBit = 0;
        private const ushort DefaultExternalTriggerGuardRegister = 500;
        private const byte DefaultExternalTriggerGuardBit = 0;
        private const ushort DefaultExternalTriggerGuardValue = 1;
        private const ushort DefaultExternalTriggerFireRegister = 550;
        private const ushort DefaultExternalTriggerSignalRegister = 2000;
        private const byte DefaultExternalTriggerSignalBit = 0;
        private const ushort DefaultExternalTriggerReadyRegister = 1000;
        private const byte DefaultExternalTriggerReadyBit = 0;

        private string _ip = DefaultIp;
        private int _port = DefaultPort;
        private byte _slaveId = DefaultSlaveId;
        private ushort[] _triggerAddrCandidates = DefaultTriggerAddresses;
        private ushort _passRegister = DefaultPassRegister;
        private byte _passBitIndex = DefaultPassBit;
        private ushort _failRegister = DefaultFailRegister;
        private byte _failBitIndex = DefaultFailBit;
        private ushort _qrRegister = DefaultQrRegister;
        private int _qrCharCapacity = DefaultQrLength;
        private ushort _workOrderReadyRegister = DefaultWorkOrderReadyRegister;
        private ushort _workOrderMissingRegister = DefaultWorkOrderMissingRegister;
        private byte _workOrderMissingBitIndex = DefaultWorkOrderMissingBit;
        private ushort _manualTriggerRegister = DefaultManualTriggerRegister;
        private byte _manualTriggerBitIndex = DefaultManualTriggerBit;
        private ushort _externalTriggerGuardRegister = DefaultExternalTriggerGuardRegister;
        private byte _externalTriggerGuardBitIndex = DefaultExternalTriggerGuardBit;
        private ushort _externalTriggerGuardValue = DefaultExternalTriggerGuardValue;
        private ushort _externalTriggerFireRegister = DefaultExternalTriggerFireRegister;
        private ushort _externalTriggerSignalRegister = DefaultExternalTriggerSignalRegister;
        private byte _externalTriggerSignalBitIndex = DefaultExternalTriggerSignalBit;
        private ushort _externalTriggerReadyRegister = DefaultExternalTriggerReadyRegister;
        private byte _externalTriggerReadyBitIndex = DefaultExternalTriggerReadyBit;

        private readonly IDisposable? _optionsReloadToken;

        // --- Result stabilize controls ---
        private readonly bool _allowIrFallback = false; // ปิดไว้กันอ่านค่าค้างจาก IR (เปิดเมื่อจำเป็น)
        private readonly int _hrRecheckAttempts = 2;   // จำนวนครั้งยืนยันผล HR4112
        private readonly int _hrRecheckDelayMs = 60;  // หน่วงระหว่างอ่านซ้ำ (ms)

        // ====== Address Map (DET6 default) ======
        // External trigger flag (from machine → camera/PC)
        private const ushort EXTERNAL_TRIGGER_HR = 4096;   // HR 4096: 1=triggered, 0=idle

        // Camera result & barcode
        private const ushort RESULT_HR = 4112;       // HR 4112: 1=PASS, 2=FAIL, other=UNKNOWN
        private const ushort BARCODE_START_HR = 4114; // HR 4114.. : ASCII (2 words per char)
        private const int BARCODE_CHAR_COUNT = 23;   // 23 chars max
        private const int WORDS_PER_CHAR = 2;        // Big-endian by default

        // Legacy/Input-register (FC04) fallback (some models export result on IR 0x1000 relative)
        private const ushort BASE_IR = 0x1000;  // when device uses IR with base 0x1000

        // ====== State ======
        private TcpClient? _client;
        private IModbusMaster? _master;

        private readonly SemaphoreSlim _bus = new(1, 1);        // serialize all Modbus ops
        private readonly SemaphoreSlim _connectMux = new(1, 1); // avoid double connect

        private int _consecutiveFailures = 0;
        private DateTime _nextWarnUtc = DateTime.MinValue;

        // One-shot trigger guard
        private int _triggering = 0;
        private DateTime _nextAllowedPulseUtc = DateTime.MinValue;

        // ====== Utils ======
        private static ushort ToUShort(int v) => checked((ushort)v);
        private static byte NormalizeBitIndex(int? bit, byte fallback)
        {
            if (!bit.HasValue) return fallback;
            return (byte)Math.Clamp(bit.Value, 0, 15);
        }
        private static bool IsBitSet(ushort value, byte bitIndex) => ((value >> bitIndex) & 0x1) == 1;

        private const int BASE = 0x1000;
        private static ushort A(int addr)
        {
            int i = (addr >= BASE) ? addr - BASE : addr;
            if (i < 0 || i > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(addr));
            return (ushort)i;
        }

        public ModbusService(IOptionsMonitor<ModbusOptions> optionsMonitor)
        {
            if (optionsMonitor == null) throw new ArgumentNullException(nameof(optionsMonitor));

            ApplyOptions(optionsMonitor.CurrentValue);
            _optionsReloadToken = optionsMonitor.OnChange(opts =>
            {
                ApplyOptions(opts);
                WarnThrottled($"[INFO] Modbus configuration reloaded (IP={_ip}, Port={_port}, Slave={_slaveId}, Pass=D{_passRegister}.{_passBitIndex}, Fail=D{_failRegister}.{_failBitIndex}, QR=D{_qrRegister}(len={_qrCharCapacity}), Ready=D{_workOrderReadyRegister}, Missing=D{_workOrderMissingRegister}.{_workOrderMissingBitIndex}, Manual=D{_manualTriggerRegister}.{_manualTriggerBitIndex}, ExternalGuard=D{_externalTriggerGuardRegister}.{_externalTriggerGuardBitIndex}=={_externalTriggerGuardValue}, ExternalFire=D{_externalTriggerFireRegister}, ExternalSignal=D{_externalTriggerSignalRegister}.{_externalTriggerSignalBitIndex}, ExternalReady=D{_externalTriggerReadyRegister}.{_externalTriggerReadyBitIndex})");
            });
        }

        private void ApplyOptions(ModbusOptions options)
        {
            var newIp = string.IsNullOrWhiteSpace(options.Ip) ? DefaultIp : options.Ip.Trim();
            var newPort = options.Port <= 0 ? DefaultPort : options.Port;
            var newSlaveId = (byte)Math.Clamp(options.SlaveId, byte.MinValue, byte.MaxValue);
            var newTriggers = (options.TriggerAddresses?.Length ?? 0) > 0
                ? options.TriggerAddresses.ToArray()
                : DefaultTriggerAddresses;
            var passRegister = ToUShort(options.PassRegister > 0 ? options.PassRegister : DefaultPassRegister);
            var failRegister = ToUShort(options.FailRegister > 0 ? options.FailRegister : DefaultFailRegister);
            var passBit = NormalizeBitIndex(options.PassBit, DefaultPassBit);
            var failBit = NormalizeBitIndex(options.FailBit, DefaultFailBit);
            var qrRegister = ToUShort(options.QrRegister > 0 ? options.QrRegister : DefaultQrRegister);
            var qrLength = options.QrLength <= 0 ? DefaultQrLength : options.QrLength;
            qrLength = Math.Clamp(qrLength, 1, 256);
            var workOrderReadyRegister = options.WorkOrderReadyRegister >= 0
                ? ToUShort(options.WorkOrderReadyRegister)
                : DefaultWorkOrderReadyRegister;
            var workOrderMissingRegister = options.WorkOrderMissingRegister >= 0
                ? ToUShort(options.WorkOrderMissingRegister)
                : DefaultWorkOrderMissingRegister;
            var workOrderMissingBit = NormalizeBitIndex(options.WorkOrderMissingBit, DefaultWorkOrderMissingBit);
            var manualTriggerRegister = options.ManualTriggerRegister >= 0
                ? ToUShort(options.ManualTriggerRegister)
                : DefaultManualTriggerRegister;
            var manualTriggerBit = NormalizeBitIndex(options.ManualTriggerBit, DefaultManualTriggerBit);
            var externalTriggerGuardRegister = options.ExternalTriggerGuardRegister >= 0
                ? ToUShort(options.ExternalTriggerGuardRegister)
                : DefaultExternalTriggerGuardRegister;
            var externalTriggerGuardBit = NormalizeBitIndex(options.ExternalTriggerGuardBit, DefaultExternalTriggerGuardBit);
            var externalTriggerGuardValue = options.ExternalTriggerGuardValue >= 0
                ? ToUShort(options.ExternalTriggerGuardValue)
                : DefaultExternalTriggerGuardValue;
            var externalTriggerFireRegister = options.ExternalTriggerFireRegister >= 0
                ? ToUShort(options.ExternalTriggerFireRegister)
                : DefaultExternalTriggerFireRegister;
            var externalTriggerSignalRegister = options.ExternalTriggerSignalRegister >= 0
                ? ToUShort(options.ExternalTriggerSignalRegister)
                : DefaultExternalTriggerSignalRegister;
            var externalTriggerSignalBit = NormalizeBitIndex(options.ExternalTriggerSignalBit, DefaultExternalTriggerSignalBit);
            var externalTriggerReadyRegister = options.ExternalTriggerReadyRegister >= 0
                ? ToUShort(options.ExternalTriggerReadyRegister)
                : DefaultExternalTriggerReadyRegister;
            var externalTriggerReadyBit = NormalizeBitIndex(options.ExternalTriggerReadyBit, DefaultExternalTriggerReadyBit);

            var ipChanged = !string.Equals(_ip, newIp, StringComparison.Ordinal);
            var portChanged = _port != newPort;

            _ip = newIp;
            _port = newPort;
            _slaveId = newSlaveId;
            _triggerAddrCandidates = newTriggers;
            _passRegister = passRegister;
            _failRegister = failRegister;
            _passBitIndex = passBit;
            _failBitIndex = failBit;
            _qrRegister = qrRegister;
            _qrCharCapacity = qrLength;
            _workOrderReadyRegister = workOrderReadyRegister;
            _workOrderMissingRegister = workOrderMissingRegister;
            _workOrderMissingBitIndex = workOrderMissingBit;
            _manualTriggerRegister = manualTriggerRegister;
            _manualTriggerBitIndex = manualTriggerBit;
            _externalTriggerGuardRegister = externalTriggerGuardRegister;
            _externalTriggerGuardBitIndex = externalTriggerGuardBit;
            _externalTriggerGuardValue = externalTriggerGuardValue;
            _externalTriggerFireRegister = externalTriggerFireRegister;
            _externalTriggerSignalRegister = externalTriggerSignalRegister;
            _externalTriggerSignalBitIndex = externalTriggerSignalBit;
            _externalTriggerReadyRegister = externalTriggerReadyRegister;
            _externalTriggerReadyBitIndex = externalTriggerReadyBit;

            if (ipChanged || portChanged)
            {
                Close(); // force reconnect with new endpoint
            }
        }

        // ========= Connection =========
        private async Task EnsureConnectedAsync()
        {
            if (_client != null && _client.Connected && _master != null) return;

            await _connectMux.WaitAsync();
            try
            {
                if (_client != null && _client.Connected && _master != null) return;

                Close();

                var c = new TcpClient { NoDelay = true, ReceiveTimeout = 2000, SendTimeout = 2000 };
                try { c.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }
                await c.ConnectAsync(_ip, _port);
                EnableTcpKeepAlive(c.Client, 20000, 2000);

                var m = ModbusIpMaster.CreateIp(c);
                m.Transport.ReadTimeout = 2000;
                m.Transport.WriteTimeout = 2000;
                m.Transport.Retries = 0;                 // we implement our own retry once
                m.Transport.WaitToRetryMilliseconds = 0;

                _client = c;
                _master = m;
            }
            finally
            {
                _connectMux.Release();
            }
        }

        private void Close()
        {
            try { _master?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            _master = null;
            _client = null;
        }

        private static bool IsTransport(Exception ex)
            => ex is IOException || ex is SocketException || ex is TimeoutException;

        private static string Short(string s) => s?.Length > 160 ? s[..160] + "…" : (s ?? "");

        private async Task ExecGroupAsync(Func<IModbusMaster, Task> body, bool retryOnceOnTransportError = true)
        {
            await _bus.WaitAsync();
            try
            {
                await EnsureConnectedAsync();
                await body(_master!);
                _consecutiveFailures = 0;
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                _consecutiveFailures++;
                WarnThrottled($"[WARN] transport error ({_consecutiveFailures}): {Short(ex.Message)} → reconnect");
                Close();

                if (!retryOnceOnTransportError) return;

                await EnsureConnectedAsync();
                await body(_master!);
                _consecutiveFailures = 0;
            }
            finally
            {
                _bus.Release();
            }
        }

        private async Task<T> ExecOnceAsync<T>(Func<IModbusMaster, T> body, bool retryOnceOnTransportError = true)
        {
            await _bus.WaitAsync();
            try
            {
                await EnsureConnectedAsync();
                var r = body(_master!);
                _consecutiveFailures = 0;
                return r;
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                _consecutiveFailures++;
                WarnThrottled($"[WARN] transport error ({_consecutiveFailures}): {Short(ex.Message)} → reconnect");
                Close();

                if (!retryOnceOnTransportError)
                    throw;

                await EnsureConnectedAsync();
                var r = body(_master!);
                _consecutiveFailures = 0;
                return r;
            }
            finally
            {
                _bus.Release();
            }
        }

        // ========= Public APIs (used by UI) =========

        /// <summary>
        /// Fire one-shot trigger via candidate COILs. Debounced by minGapMs.
        /// </summary>
        public async Task<bool> TriggerOnceAsync(int pulseMs = 120, int minGapMs = 1500)
        {
            Console.WriteLine($"[DEBUG] TriggerOnceAsync called at {DateTime.Now:HH:mm:ss.fff}");
            if (DateTime.UtcNow < _nextAllowedPulseUtc)
            {
                Console.WriteLine("[DEBUG] Blocked by cooldown");
                return false;
            }
            if (Interlocked.Exchange(ref _triggering, 1) == 1)
            {
                Console.WriteLine("[DEBUG] Blocked by triggering lock");
                return false;
            }

            try
            {
                bool ok = false;
                await ExecGroupAsync(async m =>
                {
                    foreach (var addr in _triggerAddrCandidates)
                    {
                        if (ok) break;
                        try
                        {
                            Console.WriteLine($"[DEBUG] Attempting trigger at address 0x{addr:X4}");
                            m.WriteSingleCoil(_slaveId, addr, true);
                            await Task.Delay(pulseMs);
                            m.WriteSingleCoil(_slaveId, addr, false);
                            Console.WriteLine($"[DEBUG] Trigger successful at 0x{addr:X4}");
                            ok = true;
                        }
                        catch (SlaveException ex)
                        {
                            Console.WriteLine($"[DEBUG] Coil @0x{addr:X4} not supported: {ex.Message}");
                        }
                    }
                }, retryOnceOnTransportError: false);

                if (ok) _nextAllowedPulseUtc = DateTime.UtcNow.AddMilliseconds(minGapMs);
                return ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] TriggerOnceAsync error: {ex.Message}");
                return false;
            }
            finally
            {
                Volatile.Write(ref _triggering, 0);
            }
        }

        /// <summary>
        /// Back-compat alias.
        /// </summary>
        public Task TriggerAsync(int pulseMs = 120) => TriggerOnceAsync(pulseMs);

        /// <summary>
        /// Momentarily asserts the configured manual-trigger bit (e.g., D102.0) to mimic a PLC pushbutton.
        /// </summary>
        public async Task<bool> PulseManualTriggerBitAsync(int pulseMs = 120)
        {
            try
            {
                await ExecGroupAsync(async m =>
                {
                    ushort addr = _manualTriggerRegister;
                    byte bit = _manualTriggerBitIndex;
                    ushort mask = (ushort)(1 << bit);

                    ushort original = 0;

                    try
                    {
                        var regs = m.ReadHoldingRegisters(_slaveId, addr, 1);
                        if (regs.Length > 0)
                        {
                            original = regs[0];
                        }
                    }
                    catch (SlaveException ex)
                    {
                        Console.WriteLine($"❌ PulseManualTriggerBitAsync read error: FC:{ex.FunctionCode} Code:{(int)ex.SlaveExceptionCode}");
                        throw;
                    }

                    ushort asserted = (ushort)(original | mask);
                    m.WriteSingleRegister(_slaveId, addr, asserted);
                    await Task.Delay(pulseMs);
                    m.WriteSingleRegister(_slaveId, addr, original);
                }, retryOnceOnTransportError: false);

                Console.WriteLine($"🚀 Manual trigger bit pulsed @D{_manualTriggerRegister}.{_manualTriggerBitIndex}");
                return true;
            }
            catch (SlaveException ex)
            {
                Console.WriteLine($"❌ PulseManualTriggerBitAsync SlaveError: FC:{ex.FunctionCode} Code:{(int)ex.SlaveExceptionCode}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ PulseManualTriggerBitAsync Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Pulses an arbitrary holding-register bit by setting it high for <paramref name="pulseMs"/> milliseconds
        /// before restoring the original register value.
        /// </summary>
        /// <param name="register">The D-register/holding-register index (e.g., 2001).</param>
        /// <param name="bitIndex">Bit position inside the register (0-15).</param>
        /// <param name="pulseMs">Duration to hold the bit HIGH before restoring the original value.</param>
        public Task<bool> PulseRegisterBitAsync(int register, int bitIndex, int pulseMs = 120)
            => PulseRegisterBitAsync(ToUShort(register), NormalizeBitIndex(bitIndex, (byte)0), pulseMs);

        /// <inheritdoc cref="PulseRegisterBitAsync(int,int,int)"/>
        public async Task<bool> PulseRegisterBitAsync(ushort register, byte bitIndex, int pulseMs = 120)
        {
            try
            {
                await ExecGroupAsync(async m =>
                {
                    ushort mask = (ushort)(1 << bitIndex);
                    ushort original = 0;

                    try
                    {
                        var regs = m.ReadHoldingRegisters(_slaveId, register, 1);
                        if (regs.Length > 0)
                        {
                            original = regs[0];
                        }
                    }
                    catch (SlaveException ex)
                    {
                        Console.WriteLine($"❌ PulseRegisterBitAsync read error @D{register}.{bitIndex}: FC:{ex.FunctionCode} Code:{(int)ex.SlaveExceptionCode}");
                        throw;
                    }

                    ushort asserted = (ushort)(original | mask);
                    m.WriteSingleRegister(_slaveId, register, asserted);
                    await Task.Delay(pulseMs);
                    m.WriteSingleRegister(_slaveId, register, original);
                }, retryOnceOnTransportError: false);

                Console.WriteLine($"⚡ PulseRegisterBitAsync success @D{register}.{bitIndex}");
                return true;
            }
            catch (SlaveException ex)
            {
                Console.WriteLine($"❌ PulseRegisterBitAsync SlaveError @D{register}.{bitIndex}: FC:{ex.FunctionCode} Code:{(int)ex.SlaveExceptionCode}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ PulseRegisterBitAsync Error @D{register}.{bitIndex}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Writes a value to the configured external-trigger fire register (e.g., MOV 1 → D550).
        /// When <paramref name="releaseAfter"/> is <c>true</c>, the register is restored after
        /// <paramref name="pulseMs"/> milliseconds so the PLC can re-arm on the next cycle.
        /// </summary>
        public async Task<bool> WriteExternalTriggerFireRegisterAsync(ushort value = 1, int pulseMs = 120, bool releaseAfter = false)
        {
            try
            {
                await ExecGroupAsync(async m =>
                {
                    ushort addr = _externalTriggerFireRegister;
                    ushort original = 0;

                    if (releaseAfter)
                    {
                        try
                        {
                            var regs = m.ReadHoldingRegisters(_slaveId, addr, 1);
                            if (regs.Length > 0)
                            {
                                original = regs[0];
                            }
                        }
                        catch (SlaveException ex)
                        {
                            Console.WriteLine($"❌ WriteExternalTriggerFireRegisterAsync read error: FC:{ex.FunctionCode} Code:{(int)ex.SlaveExceptionCode}");
                            throw;
                        }
                    }

                    m.WriteSingleRegister(_slaveId, addr, value);

                    if (releaseAfter)
                    {
                        await Task.Delay(Math.Max(0, pulseMs));
                        m.WriteSingleRegister(_slaveId, addr, original);
                    }
                }, retryOnceOnTransportError: false);

                Console.WriteLine($"🚦 External trigger fire register written D{_externalTriggerFireRegister}={value}");
                return true;
            }
            catch (SlaveException ex)
            {
                Console.WriteLine($"❌ WriteExternalTriggerFireRegisterAsync SlaveError: FC:{ex.FunctionCode} Code:{(int)ex.SlaveExceptionCode}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ WriteExternalTriggerFireRegisterAsync Error: {ex.Message}");
                return false;
            }
        }

        // ===== Result read with stabilization =====

        private static string MapResult(ushort v) => v switch
        {
            1 => "PASS",
            2 => "FAIL",
            _ => "UNKNOWN"
        };

        private async Task<string> ReadResultHrOnceAsync()
        {
            var hr = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, ToUShort(RESULT_HR), 1));
            return MapResult(hr[0]);
        }

        /// <summary>
        /// อ่าน HR4112 แบบ “กันค่าแกว่ง”: อ่านซ้ำก่อนคอมมิตผล
        /// - ถ้าได้ PASS/FAIL ครั้งแรก → อ่านซ้ำยืนยันว่าตรงกัน จึงคืนค่า
        /// - ถ้าครั้งแรก UNKNOWN → ลองอ่านซ้ำ ถ้าได้ PASS/FAIL ก็รับค่านั้น
        /// - ไม่งั้นคืน UNKNOWN
        /// </summary>
        private async Task<string> ReadResultHrWithStabilizeAsync(int attempts, int delayMs)
        {
            if (attempts <= 1) return await ReadResultHrOnceAsync();

            string first = await ReadResultHrOnceAsync();
            if (first == "PASS" || first == "FAIL")
            {
                await Task.Delay(delayMs);
                string second = await ReadResultHrOnceAsync();
                return (second == first) ? first : "UNKNOWN";
            }

            // ครั้งแรก UNKNOWN → ลองอ่านซ้ำ
            await Task.Delay(delayMs);
            string retry = await ReadResultHrOnceAsync();
            return (retry == "PASS" || retry == "FAIL") ? retry : "UNKNOWN";
        }

        /// <summary>
        /// อ่าน PASS/FAIL จาก HR4112 โดยยืนยันผลก่อน แล้วค่อยพิจารณา IR (ตามสวิตช์)
        /// </summary>
        public async Task<string> ReadResultStatusAsync(bool clearAfter = false)
        {
            // 1) พยายามจาก HR4112 ก่อน (แบบกันค่าแกว่ง)
            try
            {
                string s = await ReadResultHrWithStabilizeAsync(_hrRecheckAttempts, _hrRecheckDelayMs);
                if (s == "PASS" || s == "FAIL")
                {
                    if (clearAfter) await ClearResultRegisterAsync();
                    return s;
                }
            }
            catch (SlaveException)
            {
                // ไปต่อ IR ได้ (ตามสวิตช์)
            }
            catch (Exception ex)
            {
                WarnThrottled($"[WARN] ReadResult HR error: {Short(ex.Message)}");
            }

            // 2) ถ้า HR ยัง UNKNOWN และ “อนุญาต” ให้ fallback → ลอง IR ที่ BASE_IR
            if (_allowIrFallback)
            {
                try
                {
                    var ir = await ExecOnceAsync(m => m.ReadInputRegisters(_slaveId, BASE_IR, 1));
                    string s = MapResult(ir[0]);
                    if ((s == "PASS" || s == "FAIL") && clearAfter)
                        await ClearResultRegisterAsync();
                    return s;
                }
                catch (Exception ex)
                {
                    WarnThrottled($"[WARN] ReadResult IR error: {Short(ex.Message)}");
                }
            }

            // 3) สรุปไม่ได้ → UNKNOWN
            var (pass, fail) = await ReadPassFailSignalsAsync();
            if (pass && !fail) return "PASS";
            if (fail && !pass) return "FAIL";
            if (pass && fail) return "FAIL";

            return "UNKNOWN";
        }

        /// <summary>
        /// Poll-friendly external trigger flag: HR 4096 == 1 → ON.
        /// </summary>
        public async Task<bool> IsExternalTriggerOnAsync()
        {
            try
            {
                var h = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, ToUShort(EXTERNAL_TRIGGER_HR), 1));
                return h.Length > 0 && h[0] == 1;
            }
            catch (Exception ex)
            {
                WarnThrottled($"[WARN] Trigger read failed: {Short(ex.Message)}");
                return false;
            }
        }

        /// <summary>
        /// Reset external trigger flag to 0.
        /// </summary>
        public async Task ClearTriggerRegisterAsync()
        {
            try
            {
                await ExecOnceAsync(m => { m.WriteSingleRegister(_slaveId, EXTERNAL_TRIGGER_HR, 0); return 0; }, retryOnceOnTransportError: false);
                Console.WriteLine("🧹 Cleared external trigger HR4096");
            }
            catch (SlaveException ex) when ((int)ex.SlaveExceptionCode == 2 || (int)ex.SlaveExceptionCode == 3)
            {
                // เงียบ: อุปกรณ์ไม่อนุญาตให้เคลียร์ ถือว่าโอเค
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ClearTriggerRegisterAsync Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear result HR 4112 (device-specific; safe no-op if not supported).
        /// </summary>
        public async Task ClearResultRegisterAsync()
        {
            try
            {
                await ExecOnceAsync(m => { m.WriteSingleRegister(_slaveId, ToUShort(RESULT_HR), 0); return 0; }, retryOnceOnTransportError: false);
                Console.WriteLine("🧹 Cleared result HR 4112");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to clear 4112: {ex.Message}");
            }
        }

        public async Task SetWorkOrderReadyAsync(bool ready)
        {
            ushort value = ready ? (ushort)1 : (ushort)0;

            try
            {
                await ExecOnceAsync(m =>
                {
                    m.WriteSingleRegister(_slaveId, _workOrderReadyRegister, value);
                    return 0;
                });

                Console.WriteLine($"🔔 Work-order ready flag {(ready ? "ON" : "OFF")} @D{_workOrderReadyRegister}");
            }
            catch (SlaveException ex)
            {
                Console.WriteLine($"❌ SetWorkOrderReadyAsync SlaveError: FC:{ex.FunctionCode} Code:{(int)ex.SlaveExceptionCode}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SetWorkOrderReadyAsync Error: {ex.Message}");
                throw;
            }
        }

        public async Task<bool?> ReadWorkOrderReadyAsync()
        {
            try
            {
                ushort[] regs = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, _workOrderReadyRegister, 1));
                if (regs.Length > 0)
                {
                    return regs[0] != 0;
                }
            }
            catch (Exception ex)
            {
                WarnThrottled($"[WARN] Work-order ready read failed: {Short(ex.Message)}");
            }

            return null;
        }

        public async Task<bool?> ReadWorkOrderMissingFlagAsync()
        {
            try
            {
                ushort[] regs = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, _workOrderMissingRegister, 1));
                if (regs.Length > 0)
                {
                    return IsBitSet(regs[0], _workOrderMissingBitIndex);
                }
            }
            catch (Exception ex)
            {
                WarnThrottled($"[WARN] Work-order missing bit read failed: {Short(ex.Message)}");
            }

            return null;
        }

        /// <summary>
        /// Reads the configured guard register (e.g., D500) so callers can evaluate the
        /// PLC handshake before issuing an external trigger command.
        /// </summary>
        public async Task<ushort?> ReadExternalTriggerGuardRegisterAsync()
        {
            try
            {
                ushort[] regs = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, _externalTriggerGuardRegister, 1));
                if (regs.Length > 0)
                {
                    return regs[0];
                }
            }
            catch (Exception ex)
            {
                WarnThrottled($"[WARN] External trigger guard read failed: {Short(ex.Message)}");
            }

            return null;
        }

        /// <summary>
        /// Reads the PLC bit that signals an external trigger request (e.g., D2000.0).
        /// Returns <c>null</c> when the read fails.
        /// </summary>
        public async Task<bool?> ReadExternalTriggerSignalAsync()
        {
            try
            {
                ushort[] regs = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, _externalTriggerSignalRegister, 1));
                if (regs.Length > 0)
                {
                    return IsBitSet(regs[0], _externalTriggerSignalBitIndex);
                }
            }
            catch (Exception ex)
            {
                WarnThrottled($"[WARN] External trigger signal read failed: {Short(ex.Message)}");
            }

            return null;
        }

        /// <summary>
        /// Reads the PLC bit that reports camera ready status (e.g., D1000.0).
        /// Returns <c>null</c> when the read fails.
        /// </summary>
        public async Task<bool?> ReadExternalTriggerReadyBitAsync()
        {
            try
            {
                ushort[] regs = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, _externalTriggerReadyRegister, 1));
                if (regs.Length > 0)
                {
                    return IsBitSet(regs[0], _externalTriggerReadyBitIndex);
                }
            }
            catch (Exception ex)
            {
                WarnThrottled($"[WARN] Trigger-ready bit read failed: {Short(ex.Message)}");
            }

            return null;
        }

        /// <summary>
        /// Reads the configured guard register (e.g., D500) to determine whether an external trigger
        /// pulse is currently authorized.
        /// </summary>
        public async Task<bool> IsExternalTriggerGuardActiveAsync()
        {
            var guardValue = await ReadExternalTriggerGuardRegisterAsync();
            if (!guardValue.HasValue) return false;

            bool matchesValue = guardValue.Value == _externalTriggerGuardValue;
            bool matchesBit = IsBitSet(guardValue.Value, _externalTriggerGuardBitIndex);
            return matchesValue || matchesBit;
        }

        public (ushort register, byte bit) PassSignalBit => (_passRegister, _passBitIndex);
        public (ushort register, byte bit) FailSignalBit => (_failRegister, _failBitIndex);
        public (ushort register, int length) QrSignal => (_qrRegister, _qrCharCapacity);
        public ushort WorkOrderReadyRegister => _workOrderReadyRegister;
        public (ushort register, byte bit) WorkOrderMissingSignal => (_workOrderMissingRegister, _workOrderMissingBitIndex);
        public (ushort register, byte bit) ManualTriggerSignal => (_manualTriggerRegister, _manualTriggerBitIndex);
        public ushort ExternalTriggerGuardRegister => _externalTriggerGuardRegister;
        public byte ExternalTriggerGuardBit => _externalTriggerGuardBitIndex;
        public ushort ExternalTriggerGuardValue => _externalTriggerGuardValue;
        public ushort ExternalTriggerFireRegister => _externalTriggerFireRegister;
        public (ushort register, byte bit) ExternalTriggerSignal => (_externalTriggerSignalRegister, _externalTriggerSignalBitIndex);
        public (ushort register, byte bit) ExternalTriggerReadySignal => (_externalTriggerReadyRegister, _externalTriggerReadyBitIndex);

        public async Task<(bool pass, bool fail)> ReadPassFailSignalsAsync()
        {
            try
            {
                ushort start = Math.Min(_passRegister, _failRegister);
                ushort end = Math.Max(_passRegister, _failRegister);
                ushort count = (ushort)(end - start + 1);

                ushort[] regs = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, start, count));
                bool pass = false;
                bool fail = false;

                if (regs.Length > 0)
                {
                    int passOffset = _passRegister - start;
                    if (passOffset >= 0 && passOffset < regs.Length)
                    {
                        pass = IsBitSet(regs[passOffset], _passBitIndex);
                    }

                    int failOffset = _failRegister - start;
                    if (failOffset >= 0 && failOffset < regs.Length)
                    {
                        fail = IsBitSet(regs[failOffset], _failBitIndex);
                    }
                }

                return (pass, fail);
            }
            catch (Exception ex)
            {
                WarnThrottled($"[WARN] Pass/Fail bit read failed: {Short(ex.Message)}");
                return (false, false);
            }
        }

        public async Task<string> ReadPlcQrStringAsync()
        {
            try
            {
                int charCapacity = Math.Clamp(_qrCharCapacity, 1, 256);
                ushort registerCount = (ushort)((charCapacity + 1) / 2);
                ushort[] regs = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, _qrRegister, registerCount));
                if (regs.Length == 0) return string.Empty;

                string highFirst = ExtractPrintableString(regs, charCapacity, highByteFirst: true);
                string lowFirst = ExtractPrintableString(regs, charCapacity, highByteFirst: false);

                if (string.IsNullOrEmpty(highFirst))
                    return lowFirst;
                if (string.IsNullOrEmpty(lowFirst))
                    return highFirst;

                if (lowFirst.Length > highFirst.Length)
                    return lowFirst;
                if (highFirst.Length > lowFirst.Length)
                    return highFirst;

                bool highLooksSwapped = LooksByteSwapped(highFirst);
                bool lowLooksSwapped = LooksByteSwapped(lowFirst);

                if (highLooksSwapped && !lowLooksSwapped)
                    return lowFirst;
                if (lowLooksSwapped && !highLooksSwapped)
                    return highFirst;

                // Default to low-first ordering for equal-length payloads because most
                // Delta PLC barcode registers expose ASCII in little-endian order.
                return lowFirst;
            }
            catch (Exception ex)
            {
                WarnThrottled($"[WARN] QR read failed: {Short(ex.Message)}");
                return string.Empty;
            }
        }

        private static string ExtractPrintableString(ushort[] regs, int charCapacity, bool highByteFirst)
        {
            if (regs.Length == 0) return string.Empty;

            int byteLength = regs.Length * 2;
            Span<byte> buffer = byteLength <= 256 ? stackalloc byte[byteLength] : new byte[byteLength];

            for (int i = 0; i < regs.Length; i++)
            {
                byte high = (byte)(regs[i] >> 8);
                byte low = (byte)(regs[i] & 0xFF);

                if (highByteFirst)
                {
                    buffer[i * 2] = high;
                    buffer[i * 2 + 1] = low;
                }
                else
                {
                    buffer[i * 2] = low;
                    buffer[i * 2 + 1] = high;
                }
            }

            return SanitizeAscii(buffer, charCapacity);
        }

        private static string SanitizeAscii(ReadOnlySpan<byte> buffer, int charCapacity)
        {
            if (buffer.Length == 0) return string.Empty;

            string raw = Encoding.ASCII.GetString(buffer);
            if (raw.Length > charCapacity)
                raw = raw[..charCapacity];

            int terminator = raw.IndexOf('\0');
            if (terminator >= 0)
                raw = raw[..terminator];

            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            return new string(raw.Where(ch => ch >= 0x20 && ch <= 0x7E).ToArray()).Trim();
        }

        private static bool LooksByteSwapped(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 2)
                return false;

            int evenSeparators = 0;
            int oddSeparators = 0;

            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch is '_' or '-' or '.' or ' ')
                {
                    if ((i & 1) == 0)
                        evenSeparators++;
                    else
                        oddSeparators++;
                }
            }

            return evenSeparators > oddSeparators;
        }

        /// <summary>
        /// Quick TCP probe; if tcpOnly==false also tries to read a probe HR.
        /// </summary>
        public async Task<bool> CheckConnectionAsync(bool tcpOnly = true, int? probeDocAddress = null)
        {
            if (!await IsTcpOpenAsync(900))
            {
                Close();
                return false;
            }
            if (tcpOnly) return true;

            try
            {
                await EnsureConnectedAsync();
                ushort addr = ToUShort(probeDocAddress ?? RESULT_HR);
                ushort[] v = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, addr, 1));
                return v.Length > 0;
            }
            catch (SlaveException sex) when (
                   (int)sex.SlaveExceptionCode == 2  // Illegal Data Address
                || (int)sex.SlaveExceptionCode == 4) // Slave Device Failure
            {
                // Device reachable but register not available → still treat as connected
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Read ASCII barcode from HR 4114.. (2 words per char). Cleans to printable ASCII.
        /// If high-byte read yields empty, fallback to low-byte extraction.
        /// </summary>
        public async Task<string> ReadCameraBarcodeAsync(
            ushort startDocAddress = BARCODE_START_HR, int charCount = BARCODE_CHAR_COUNT, int wordsPerChar = WORDS_PER_CHAR, int settleDelayMs = 80)
        {
            if (settleDelayMs > 0) await Task.Delay(settleDelayMs);

            ushort start = ToUShort(startDocAddress);
            ushort totalWords = ToUShort(charCount * wordsPerChar);
            ushort[] regs = await ReadHoldingAsync(start, totalWords);

            // Try high-byte path first
            var sb = new StringBuilder(charCount);
            for (int i = 0; i + 1 < regs.Length; i += 2)
            {
                char ch = (char)regs[i];
                if (ch == '\0') break;
                sb.Append(ch);
            }
            if (sb.Length == 0)
            {
                // Fallback to low-byte
                for (int i = 0; i + 1 < regs.Length; i += 2)
                {
                    char ch = (char)(regs[i] & 0x00FF);
                    if (ch == '\0') break;
                    sb.Append(ch);
                }
            }

            string raw = sb.ToString();
            string clean = new string(raw.Where(ch => ch >= 0x20 && ch <= 0x7E).ToArray()).Trim();
            return clean;
        }

        public Task<ushort[]> ReadHoldingAsync(ushort startAddress, ushort count)
            => ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, startAddress, count));

        public async Task<string> GetCameraConnectionStatusAsync()
        {
            try
            {
                if (_client == null || !_client.Connected)
                    return "Disconnected: No TCP connection";

                ushort[] status = await ExecOnceAsync(m => m.ReadHoldingRegisters(_slaveId, ToUShort(RESULT_HR), 1));
                int value = status.Length > 0 ? status[0] : 0;
                return (value == 0 || status.Length == 0)
                    ? "Disconnected: No camera response"
                    : "Connected: Camera is online";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GetCameraConnectionStatusAsync Error: {ex.Message}");
                return "Disconnected: Error checking connection";
            }
        }

        // ========= Low-level helpers =========
        private async Task<bool> IsTcpOpenAsync(int timeoutMs = 900)
        {
            try
            {
                using var c = new TcpClient();
                var t = c.ConnectAsync(_ip, _port);
                var done = await Task.WhenAny(t, Task.Delay(timeoutMs));
                return done == t && c.Connected;
            }
            catch { return false; }
        }

        public void Dispose()
        {
            _optionsReloadToken?.Dispose();
            Close();
        }

        private static void EnableTcpKeepAlive(Socket sock, int keepAliveTimeMs, int keepAliveIntervalMs)
        {
            try
            {
                sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                var opt = new byte[12];
                BitConverter.GetBytes((uint)1).CopyTo(opt, 0);
                BitConverter.GetBytes((uint)keepAliveTimeMs).CopyTo(opt, 4);
                BitConverter.GetBytes((uint)keepAliveIntervalMs).CopyTo(opt, 8);
                sock.IOControl(IOControlCode.KeepAliveValues, opt, null);
            }
            catch { /* ignore */ }
        }

        private void WarnThrottled(string msg, int intervalMs = 2500)
        {
            if (DateTime.UtcNow >= _nextWarnUtc)
            {
                Console.WriteLine(msg);
                _nextWarnUtc = DateTime.UtcNow.AddMilliseconds(intervalMs);
            }
        }

        private static async Task<bool> TryPulseRegisterAsync(
            IModbusMaster m,
            byte slaveId,
            ushort addr,
            int onVal,
            int offVal,
            int pulseMs,
            string logOk,
            string logFailPrefix)
        {
            try
            {
                m.WriteSingleRegister(slaveId, addr, (ushort)onVal);
                await Task.Delay(pulseMs);
                m.WriteSingleRegister(slaveId, addr, (ushort)offVal);
                Console.WriteLine(logOk);
                return true;
            }
            catch (SlaveException ex)
            {
                Console.WriteLine($"{logFailPrefix}: FC:{ex.FunctionCode} Code:{(int)ex.SlaveExceptionCode}");
                // หรือถ้าต้องการขึ้นบรรทัดใหม่:
                // Console.WriteLine($"{logFailPrefix}: FC:{ex.FunctionCode}\nCode:{(int)ex.SlaveExceptionCode}");
                return false;
            }
        }


        /// <summary>
        /// Pulse HR4096 from 0 → delay → 1 **atomically inside the bus lock**.
        /// </summary>
        public async Task<bool> RaiseExternalTriggerAsync()
        {
            try
            {
                await ExecGroupAsync(async m =>
                {
                    // reset ก่อน
                    m.WriteSingleRegister(_slaveId, EXTERNAL_TRIGGER_HR, 0);
                    await Task.Delay(50);
                    // จึงค่อยกด 1
                    m.WriteSingleRegister(_slaveId, EXTERNAL_TRIGGER_HR, 1);
                }, retryOnceOnTransportError: false);

                Console.WriteLine("🧪 RaiseExternalTriggerAsync: HR4096 pulse 0→1");
                return true;
            }
            catch (SlaveException ex)
            {
                Console.WriteLine($"❌ RaiseExternalTriggerAsync SlaveError: FC:{ex.FunctionCode} Code:{(int)ex.SlaveExceptionCode}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ RaiseExternalTriggerAsync Error: {ex.Message}");
                return false;
            }
        }
    }
}
