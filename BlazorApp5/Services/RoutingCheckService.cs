using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorApp5.Helpers;

public class RoutingCheckService
{
    private readonly HttpClient _http;
    private const string Factory = "DET6";
    private const string TestType = "ROUTING_UPDATE";
    private const string SecretKey = "894A0F0DF84A4799E0530CCA940AC604";
    private const string UrlBase = "http://THWGRMESEP01.deltaww.com:10101/TDC/DELTA_DEAL_TEST_DATA_I";

    public RoutingCheckService(HttpClient http)
    {
        _http = http;
    }

    public async Task<(bool isSuccess, string rawResponse, string url, string jsonBody)> UpdateRoutingAsync(
        string qrCode,
        string workOrder,
        string model,
        string inputLine,
        string sectionName,
        string group,
        string station,
        string empNo,
        string inspectionResult)
    {
        try
        {
            // ✅ ประกอบ routingData (18 ช่อง)
            string[] routingFields = new string[18]
            {
            qrCode ?? "",
            workOrder?.Trim() ?? "",
            model?.Replace(" ", "").Trim() ?? "",
            inputLine?.Trim() ?? "",
            sectionName?.Trim() ?? "",
            group?.Trim() ?? "",
            station?.Trim() ?? "",
            empNo?.Trim() ?? "",
            inspectionResult?.Trim() ?? "",
            workOrder?.Trim() ?? "",
            model?.Replace(" ", "").Trim() ?? "",
            inputLine?.Trim() ?? "",
            station?.Trim() ?? "",
            "", "", "", "", ""
            };

            string routingData = string.Join("}", routingFields) + "}";
            string jsonBody = $"{{\"factory\":\"{Factory}\",\"testType\":\"{TestType}\",\"routingData\":\"{routingData}\",\"testData\":[]}}";

            string sign = MD5Helper.Generate(SecretKey + jsonBody);
            string url = $"{UrlBase}?sign={sign}";

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("TokenID", SecretKey);

            var response = await _http.SendAsync(request);
            string rawResponse = await response.Content.ReadAsStringAsync();

            // ✅ พิจารณาความสำเร็จจากเนื้อหา (คุณอาจใช้ .Contains("result\":\"PASS\"") ก็ได้)
            bool isSuccess = rawResponse.Contains("\"result\":\"PASS\"") || rawResponse.Contains("\"result\":\"OK\"");

            return (isSuccess, rawResponse, url, jsonBody);
        }
        catch (Exception ex)
        {
            string errorMsg = $"❌ Exception: {ex.Message}";
            return (false, errorMsg, "", "");
        }
    }

    public async Task<(bool isPass, string message)> CheckRoutingAsync(string qrCode)
    {
        try
        {
            string url = $"http://THWGRMESEP01.deltaww.com:10101/TDC/ROUTING_CHECK?barcode={qrCode}";

            var response = await _http.GetAsync(url);
            string content = await response.Content.ReadAsStringAsync();

            var json = JsonDocument.Parse(content);
            var root = json.RootElement;

            string result = root.GetProperty("Result").GetString() ?? "";
            string message = root.GetProperty("Message").GetString() ?? "";

            return result == "PASS"
                ? (true, message)
                : (false, message);
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}");
        }
    }
}
