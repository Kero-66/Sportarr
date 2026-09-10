# NZBdav

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/nzbdav.svg" alt="" width="72" height="72" />
</p>

Usenet streaming via WebDAV, exposed through a SABnzbd-compatible API, so completed downloads mount instead of occupying local disk.

| | |
|---|---|
| Protocol | Usenet |
| Default port | 3000 |
| Authentication | API key (SABnzbd-compatible) |

## Setup

1. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **NZBdav**
2. Enter the host, port, and API key
3. Keep the category as `sportarr`
4. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).

## Completed download cleanup

With **Remove Completed Downloads** enabled, Sportarr checks the imported job's history entry and requests its removal. A queue response alone does not count as successful history cleanup. If history removal fails, Sportarr logs a warning and skips any remaining download folder cleanup. The import remains marked as successful.

Removing queue or history entries preserves NZBdav's completed virtual files, which imported library links may still use. Removing a queued job while keeping files is supported.
