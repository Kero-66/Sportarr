# SABnzbd

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/sabnzbd.svg" alt="" width="72" height="72" />
</p>

Open source binary newsreader, the standard choice for usenet.

| | |
|---|---|
| Protocol | Usenet |
| Default port | 8080 |
| Authentication | API key |

## Setup

1. Copy the API key from SABnzbd under **Config > General > Security**
2. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **SABnzbd**
3. Enter the host, port, and API key. If SABnzbd runs under a URL base like `/sabnzbd`, set it in **URL Base**
4. Keep the category as `sportarr`
5. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).

## Completion notifications

An integration can send the job's `nzo_id` to Sportarr's
[completion endpoint](../APPLICATION_API.md#download-completion-notifications)
after SABnzbd reports it complete.

A synchronous post-processing script runs before SABnzbd marks the job
complete. A callback from that script can arrive too early and leave the
import waiting for normal polling. SABnzbd's notification script also has
different environment variables and does not receive `SAB_NZO_ID` directly.
Check the [post-processing sequence](https://github.com/sabnzbd/sabnzbd/blob/5.0.4/sabnzbd/postproc.py#L604-L717)
and [notification-script contract](https://sabnzbd.org/wiki/configuration/4.5/scripts/notification-scripts)
when wiring an integration.

## Download removal

Sportarr checks whether the job is queued or in history before removing it. A completed job must be removed from history before Sportarr reports successful cleanup. If removal fails, the imported library file stays available and Sportarr skips any remaining download folder cleanup.

Removing a queued SABnzbd job without deleting its files preserves the files already downloaded.
