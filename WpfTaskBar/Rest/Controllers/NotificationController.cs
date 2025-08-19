using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using WpfTaskBar.Rest.Models;

namespace WpfTaskBar
{
    [ApiController]
    public class NotificationController : ControllerBase
    {
        [HttpPost("notification")]
        public ActionResult<NotificationResponse> PostNotification([FromBody] NotificationRequest request)
        {
            NotificationModel.AddNotification(request.Title, request.Message);
            
            return Ok(new NotificationResponse 
            { 
                Success = true, 
                Message = "Notification added successfully",
                NotificationCount = NotificationModel.Notifications.Count
            });
        }

        [HttpGet("notifications")]
        public ActionResult<NotificationListResponse> GetNotifications()
        {
            var notifications = NotificationModel.Notifications.ToList();
            return Ok(new NotificationListResponse 
            { 
                Notifications = notifications,
                Count = notifications.Count
            });
        }

        [HttpDelete("notifications")]
        public ActionResult ClearNotifications()
        {
            NotificationModel.ClearNotifications();
            return Ok(new { Message = "All notifications cleared" });
        }
    }

    public class NotificationRequest
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class NotificationResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("notification_count")]
        public int NotificationCount { get; set; }
    }

    public class NotificationListResponse
    {
        [JsonPropertyName("notifications")]
        public List<NotificationItem> Notifications { get; set; } = new List<NotificationItem>();

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }
}