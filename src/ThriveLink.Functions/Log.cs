using Microsoft.Extensions.Logging;

using ThriveLink.Domain;

namespace ThriveLink.Functions;

/// <summary>
/// Source-generated log messages: no work happens when the level is disabled, and the named values
/// land in Application Insights customDimensions where KQL queries and alerts can use them.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Rejected malformed wearable payload")]
    public static partial void MalformedPayload(this ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Wearable sync for unknown member {MemberNumber} from {Provider}")]
    public static partial void UnknownMember(this ILogger logger, string memberNumber, DeviceProvider provider);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Wearable sync {MemberNumber} {Provider} {DayCount} days")]
    public static partial void WearableSynced(this ILogger logger, string memberNumber, DeviceProvider provider, int dayCount);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Weekly rewards {WeekStart}: {Achieved} of {Evaluated} members hit target")]
    public static partial void WeeklyRewardsEvaluated(this ILogger logger, DateOnly weekStart, int achieved, int evaluated);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Stale device sweep: {StaleDeviceCount} devices not synced in 72h")]
    public static partial void StaleDevices(this ILogger logger, int staleDeviceCount);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Stale devices for {Provider}: {ProviderStaleCount}")]
    public static partial void StaleDevicesForProvider(this ILogger logger, DeviceProvider provider, int providerStaleCount);
}
