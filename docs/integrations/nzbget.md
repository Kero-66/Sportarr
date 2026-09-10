# NZBGet

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/nzbget.svg" alt="" width="72" height="72" />
</p>

Efficient usenet downloader with a small resource footprint.

| | |
|---|---|
| Protocol | Usenet |
| Default port | 6789 |
| Authentication | Control username and password |

## Setup

1. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **NZBGet**
2. Enter the host, port, and the control username and password from NZBGet's settings (`ControlUsername` / `ControlPassword`)
3. Keep the category as `sportarr`
4. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).

## Completed download cleanup

With **Remove Completed Downloads** enabled, Sportarr removes an imported job from NZBGet's visible history. Its duplicate protection record is retained when NZBGet duplicate checking is enabled. If NZBGet rejects the removal, Sportarr logs a warning and skips the remaining download folder cleanup. The imported library file stays available.

A move import can remove the original download folder before the client cleanup request. A cleanup warning does not undo that move.

When you remove a queued download without deleting its files, Sportarr preserves the downloaded files. A failed or previously deleted history item can require file deletion to remove it from NZBGet. Sportarr leaves that item in place when you choose to keep files.
