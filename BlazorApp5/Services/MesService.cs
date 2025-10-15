
namespace BlazorApp5.Services

{
    using DeltaLibrary;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Web;
    using System.Threading.Tasks;
    using BlazorApp5.Helpers;

    //public class MesService
    //{
    //    MESInfo mESInfo = new MESInfo();
    //    public string SetMO()
    //    {

    //        mESInfo.GetMO();
    //        string str = mESInfo.wo + "}" + mESInfo.section + "}" + mESInfo.line + "}" + mESInfo.model + "}";

    //        return str;

    //    }

    public class MesService
    {
        public static async Task<List<MOInfo>> GetMOListAsync(HttpClient http, Action<string>? onSignGenerated = null)
        {
            string empNo = "86347852";
            string factory = "DET6";
            string getType = "1";
            string line = "F23";
            string moType = "0";
            string secret = "894A0F0DF84A4799E0530CCA940AC604"; // ใช้ key จริง

            var paramRaw = $"EMP_NO{empNo}FACTORY{factory}GETDATA_TYPE{getType}LINE_NAME{line}MO_TYPE{moType}";
            var sign = MD5Helper.Generate(secret + paramRaw);

            // ✅ ส่ง sign กลับไปให้ UI แสดง
            onSignGenerated?.Invoke(sign);

            var query = HttpUtility.ParseQueryString(string.Empty);
            query["EMP_NO"] = empNo;
            query["FACTORY"] = factory;
            query["GETDATA_TYPE"] = getType;
            query["LINE_NAME"] = line;
            query["MO_TYPE"] = moType;
            query["sign"] = sign;

            var url = $"http://10.150.192.16:10101/QueryData/MOList?{query}";

            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(jsonString);
            var message = doc.RootElement.GetProperty("Message");

            var moList = JsonSerializer.Deserialize<List<MOInfo>>(message.GetRawText());
            return moList ?? new List<MOInfo>();
        }

    }

}