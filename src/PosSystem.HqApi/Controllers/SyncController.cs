using Microsoft.AspNetCore.Mvc;
using PosSystem.HqApi.Services;

namespace PosSystem.HqApi.Controllers;

public record SyncEventRequest(int Id, string EventType, string AggregateId, string PayloadJson, DateTime CreatedAt);

[ApiController]
[Route("api/sync")]
public class SyncController : ControllerBase
{
    private readonly HqSyncStore _store;

    public SyncController(HqSyncStore store)
    {
        _store = store;
    }

    [HttpPost("events")]
    public IActionResult ReceiveEvent([FromBody] SyncEventRequest request)
    {
        var record = new SyncedEventRecord(
            request.Id,
            request.EventType,
            request.AggregateId,
            request.PayloadJson,
            request.CreatedAt,
            DateTime.Now);

        bool inserted = _store.TryAddIdempotent(record);
        return Ok(new
        {
            Accepted = true,
            DuplicateIgnored = !inserted,
            request.AggregateId,
            ReceivedAt = record.ReceivedAt
        });
    }

    [HttpGet("events")]
    public IActionResult GetSyncedEvents() => Ok(_store.GetAll());
}
