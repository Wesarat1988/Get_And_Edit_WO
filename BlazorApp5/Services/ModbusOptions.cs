namespace BlazorApp5.Services;

/// <summary>
/// Strongly-typed Modbus configuration that mirrors <c>appsettings.json</c> and
/// feeds <see cref="ModbusService"/>.  The defaults match the in-service
/// constants so the binder can omit values while local text edits (for example
/// from Notepad) still take effect without recompilation.
/// </summary>
public sealed class ModbusOptions
{
    /// <summary>PLC IP address (e.g. 192.168.1.81).</summary>
    public string Ip { get; set; } = "192.168.1.81";

    /// <summary>Modbus TCP port, typically 502.</summary>
    public int Port { get; set; } = 502;

    /// <summary>Station/slave identifier.</summary>
    public int SlaveId { get; set; } = 1;

    /// <summary>Candidate holding-register addresses that fire the capture trigger.</summary>
    public ushort[] TriggerAddresses { get; set; } = new ushort[] { 0x0000, 0x0001, 0x0002 };

    /// <summary>Holding-register index that represents the PASS bit (D3000).</summary>
    public int PassRegister { get; set; } = 3000;

    /// <summary>Bit position inside <see cref="PassRegister"/> that signals a pass.</summary>
    public int PassBit { get; set; } = 0;

    /// <summary>Holding-register index that represents the FAIL bit (D3001).</summary>
    public int FailRegister { get; set; } = 3001;

    /// <summary>Bit position inside <see cref="FailRegister"/> that signals a fail.</summary>
    public int FailBit { get; set; } = 0;

    /// <summary>Holding-register that stores the ASCII 
    /// payload (D5000).</summary>
    public int QrRegister { get; set; } = 5000;

    /// <summary>Maximum number of characters to read from <see cref="QrRegister"/>.</summary>
    public int QrLength { get; set; } = 40;

    /// <summary>Holding-register index that indicates work-order readiness (e.g., D100).</summary>
    public int WorkOrderReadyRegister { get; set; } = 100;

    /// <summary>Holding-register index whose bit reports that the PLC still requires a work order (e.g., D103).</summary>
    public int WorkOrderMissingRegister { get; set; } = 103;

    /// <summary>Bit position inside <see cref="WorkOrderMissingRegister"/> that flags a missing work order.</summary>
    public int WorkOrderMissingBit { get; set; } = 0;

    /// <summary>Holding-register index whose bit toggles when the manual test button fires (e.g., D102).</summary>
    public int ManualTriggerRegister { get; set; } = 102;

    /// <summary>Bit position inside <see cref="ManualTriggerRegister"/> that represents the manual trigger pulse.</summary>
    public int ManualTriggerBit { get; set; } = 0;

    /// <summary>Holding-register index whose value authorizes the automatic/external trigger handshake (e.g., D500).</summary>
    public int ExternalTriggerGuardRegister { get; set; } = 500;

    /// <summary>
    /// Bit position inside <see cref="ExternalTriggerGuardRegister"/> that should be asserted before
    /// the app sends an external trigger pulse.
    /// </summary>
    public int ExternalTriggerGuardBit { get; set; } = 0;

    /// <summary>
    /// Value inside <see cref="ExternalTriggerGuardRegister"/> that enables the external trigger pulse.
    /// Retained for PLCs that expose the guard as a whole-register flag instead of a single bit.
    /// </summary>
    public int ExternalTriggerGuardValue { get; set; } = 1;

    /// <summary>Holding-register index that should receive a MOV value to request a camera shot for external triggers (e.g., D550).</summary>
    public int ExternalTriggerFireRegister { get; set; } = 550;

    /// <summary>Holding-register index whose bit indicates an external trigger request (e.g., D2000).</summary>
    public int ExternalTriggerSignalRegister { get; set; } = 2000;

    /// <summary>Bit position inside <see cref="ExternalTriggerSignalRegister"/> that carries the trigger request.</summary>
    public int ExternalTriggerSignalBit { get; set; } = 0;

    /// <summary>Holding-register index whose bit reports that the camera is ready for the next trigger (e.g., D1000).</summary>
    public int ExternalTriggerReadyRegister { get; set; } = 1000;

    /// <summary>Bit position inside <see cref="ExternalTriggerReadyRegister"/> that toggles OFF/ON during the capture cycle.</summary>
    public int ExternalTriggerReadyBit { get; set; } = 0;
}
