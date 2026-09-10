using System.Linq.Expressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

public static class DownloadMonitorEligibility
{
    public const int MaxImportRetries = 3;

    public static Expression<Func<DownloadQueueItem, bool>> ActiveDownloads => download =>
        download.Status != DownloadStatus.Imported &&
        (download.Status != DownloadStatus.Failed ||
         (download.RetryCount < 3 && (download.ImportRetryCount ?? 0) < MaxImportRetries));
}
