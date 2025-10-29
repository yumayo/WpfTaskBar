using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace WpfTaskBar
{
    [ApiController]
    public class TimeRecordController : ControllerBase
    {
        private readonly WebView2Handler _webView2Handler;

        public TimeRecordController(WebView2Handler webView2Handler)
        {
            _webView2Handler = webView2Handler;
        }

        [HttpPost("clock-in")]
        public ActionResult<ClockInResponse> ClockIn([FromBody] ClockInRequest request)
        {
            TimeRecordModel.ClockInDate = request.Date;

            _webView2Handler.SendMessageToWebView(new
            {
                type = "clock_in_update",
                date = TimeRecordModel.ClockInDate,
            });

            return Ok(new ClockInResponse { ClockInDate = TimeRecordModel.ClockInDate });
        }

        [HttpPost("clock-out")]
        public ActionResult<ClockOutResponse> ClockOut([FromBody] ClockOutRequest request)
        {
            TimeRecordModel.ClockOutDate = request.Date;
            
            _webView2Handler.SendMessageToWebView(new
            {
                type = "clock_out_update",
                date = TimeRecordModel.ClockOutDate,
            });
            
            return Ok(new ClockOutResponse { ClockOutDate = TimeRecordModel.ClockOutDate });
        }
        
        [HttpPost("clear")]
        public ActionResult Clear()
        {
            TimeRecordModel.ClockInDate = default;
            TimeRecordModel.ClockOutDate = default;
            
            _webView2Handler.SendMessageToWebView(new
            {
                type = "clock_clear"
            });
            
            return Ok();
        }
    }

    public class ClockInRequest
    {
        [JsonPropertyName("date")]
        public DateTime Date { get; set; }
    }

    public class ClockOutRequest
    {
        [JsonPropertyName("date")]
        public DateTime Date { get; set; }
    }

    public class ClockInResponse
    {
        [JsonPropertyName("clock_in_date")]
        public DateTime ClockInDate { get; set; }
    }

    public class ClockOutResponse
    {
        [JsonPropertyName("clock_out_date")]
        public DateTime ClockOutDate { get; set; }
    }
}
