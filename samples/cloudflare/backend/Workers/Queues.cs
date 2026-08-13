namespace Cloudflare.Workers;

/// <summary>
/// JS <c>Queue&lt;Body&gt;</c> producer. <c>Body</c> is a JSON-copied record, not <c>unknown</c>.
/// </summary>
public interface IQueue
{
    Task<QueueSendResponse> Send(QueueMessage message);
    Task<QueueSendBatchResponse> SendBatch(QueueMessage[] messages);
    Task<QueueMetrics> Metrics();
}

/// <summary>JS <c>MessageSendRequest</c> + <c>QueueSendOptions</c> flattened onto one record.</summary>
public sealed record QueueMessage(string Body, double? DelaySeconds = null, string? ContentType = null);

/// <summary>JS <c>QueueSendResponse</c> (metadata nested in TS; flattened here).</summary>
public sealed record QueueSendResponse(int BacklogCount, int BacklogBytes);

/// <summary>JS <c>QueueSendBatchResponse</c>.</summary>
public sealed record QueueSendBatchResponse(int BacklogCount, int BacklogBytes);

/// <summary>JS <c>QueueMetrics</c>. Timestamp is epoch milliseconds.</summary>
public sealed record QueueMetrics(int BacklogCount, int BacklogBytes, double? OldestMessageTimestamp);

/// <summary>
/// JS <c>MessageBatch</c> fields the Worker <c>queue</c> handler snapshots as JSON
/// (ack/retry stay on the JS host — they must run on the batch workerd gave us).
/// </summary>
public sealed record QueueMessageSnapshot(string Id, string Body, int Attempts);
