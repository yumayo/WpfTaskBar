using Microsoft.AspNetCore.Mvc;
using WpfTaskBar.Rest.Models;

namespace WpfTaskBar
{
    [ApiController]
    [Route("api/[controller]")]
    public class TimeRecordController : ControllerBase
    {
        [HttpPost("clock-in")]
        public ActionResult<DateTime> ClockIn()
        {
            TimeRecordModel.ClockInDate = DateTime.Now;
            return Ok(TimeRecordModel.ClockInDate);
        }

        [HttpPost("clock-out")]
        public ActionResult<DateTime> ClockOut()
        {
            TimeRecordModel.ClockOutDate = DateTime.Now;
            return Ok(TimeRecordModel.ClockOutDate);
        }
        
        [HttpPost("clear")]
        public ActionResult Clear()
        {
            TimeRecordModel.ClockInDate = default;
            TimeRecordModel.ClockOutDate = default;
            return Ok();
        }
    }
}
