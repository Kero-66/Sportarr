# qBittorrent

<p align="center" class="integration-logo">
  <img src="../../assets/integrations/qbittorrent.svg" alt="" width="72" height="72" />
</p>

Free and reliable torrent client, and the most common pairing for Sportarr torrent setups.

| | |
|---|---|
| Protocol | Torrent |
| Default port | 8080 |
| Authentication | WebUI username and password |

## Setup

1. In qBittorrent, enable the WebUI under **Tools > Options > Web UI** and set a username and password
2. In Sportarr, go to **Settings > Download Clients**, click **Add**, and choose **qBittorrent**
3. Enter the host, port, and your WebUI credentials
4. Keep the category as `sportarr` so Sportarr only manages its own downloads
5. **Test**, then **Save**

Post-import modes, per-indexer client pinning, and remote path mappings are shared across all clients and documented under [Download Clients](../features/download-clients.md).

## Completion notifications

On Linux, save this executable script on the qBittorrent host or container as
`/config/scripts/sportarr-completed.sh`. Replace the URL and API key with your
Sportarr settings. The script requires `curl` in that environment.

```sh
#!/bin/sh
case "$1" in
  ''|*[!0-9a-fA-F]*) exit 1 ;;
esac
curl --fail --silent --show-error --max-time 10 \
  -H 'X-Api-Key: YOUR_SPORTARR_API_KEY' \
  -H 'Content-Type: application/json' \
  --data "{\"downloadId\":\"$1\"}" \
  http://sportarr:1867/api/download/completed
```

In **Options > Downloads**, enable **Run external program on torrent finished**
and enter `/config/scripts/sportarr-completed.sh "%K"`.

In qBittorrent 5.1.2, `%K` supplies the torrent ID. `%I` only supplies a v1
info hash, so use `%K` to cover v2 torrents too. These substitutions and the
completion trigger are defined in qBittorrent's
[command handling](https://github.com/qbittorrent/qBittorrent/blob/release-5.1.2/src/app/application.cpp#L537-L578)
and [finished handler](https://github.com/qbittorrent/qBittorrent/blob/release-5.1.2/src/app/application.cpp#L731-L737).

See the [completion API](../APPLICATION_API.md#download-completion-notifications)
for an optional client ID and response details. Regular polling still handles
downloads if the callback cannot reach Sportarr.
