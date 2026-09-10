# Initial Setup

Four steps take a fresh install to a working library.

## 1. Root folder

Go to **Settings > Media Management** and add a root folder. This is where Sportarr stores your sports library.

![Add Root Folder](../images/add-root-folder.png)

## 2. Download client

**Settings > Download Clients**. Add your download client: qBittorrent, Transmission, Deluge, rTorrent, uTorrent, SABnzbd, NZBGet, NZBdav, Decypharr, or a torrent/usenet blackhole folder.

!!! tip "Docker path alignment"
    If both apps run in Docker, make sure the download path is visible to both containers at the same path. When paths differ between hosts, set up a remote path mapping in Sportarr.

![Add Download Client](../images/add-download-client.png)

## 3. Indexers

**Settings > Indexers**. Add your Usenet indexers or torrent trackers. Sportarr supports Newznab and Torznab APIs, so [Prowlarr integration](../integrations/prowlarr.md) works out of the box.

![Add Indexer](../images/add-indexer.png)

## 4. Add content

Use the search to find leagues or events. Add them to your library and Sportarr starts monitoring.

Sportarr uses the league's competition format from the API to decide whether to show team selection. Team competitions let you choose which teams to follow. Individual event leagues, such as Diamond League athletics, use your event monitoring settings without a team filter, even when the source lists countries or federations as teams.

If an existing individual league is missing events because of saved team selections, use **More > Sync > Deep Sync** on its league page after updating. This refreshes its competition format and imports the missing historical events.

![Search for Leagues](../images/search-league.png)

![Team Selection](../images/search-league-teams.png)

![League Detail View](../images/league-detail.png)
