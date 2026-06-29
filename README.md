# AD Group User Compare

Web-App zum Auslesen und Vergleichen von Active-Directory-Benutzern aus Gruppen, deren Gruppenname einem Muster entspricht.

Die App ist die Linux-/Docker-Portierung des WPF-Tools `ad-group-user-exporter`: Browseroberflaeche statt WPF, LDAP statt Windows-RSAT-PowerShell. Gleiche Idee, anderes Bezugssystem.

## Funktionen

- Gruppenmuster setzen, z. B. `abc*_1a*`
- optional `SearchBase` und LDAP-Server angeben
- rekursive Aufloesung verschachtelter Gruppen
- optional nur aktive Benutzer ausgeben
- Ergebnis im Browser filtern
- sichtbare `GroupName`-Werte kopieren
- sichtbares Ergebnis als CSV exportieren
- zwei Benutzer aus dem geladenen Ergebnis vergleichen
- LDAP-Testdialog fuer Server, Bind, SearchBase und Gruppenmuster
- Docker- und Portainer-Stack fuer Ubuntu/Linux

## Konfiguration

Die App liest Konfiguration aus `appsettings.json`, .NET-Environment-Variablen und klassischen `AD_*`-Variablen.

| Variable | Beschreibung | Beispiel |
| --- | --- | --- |
| `AD_GROUP_USER_COMPARE_PORT` | Host-Port fuer Docker/Portainer | `3003` |
| `AD_LDAP_SERVER` oder `Ad__Server` | Domain Controller oder LDAP-Host | `dc01.example.local` |
| `AD_LDAP_PORT` oder `Ad__Port` | LDAP-Port | `389` oder `636` |
| `AD_USE_SSL` oder `Ad__UseSsl` | LDAPS aktivieren | `false` |
| `AD_SEARCH_BASE` oder `Ad__SearchBase` | Basis-DN fuer Gruppensuche | `OU=Groups,DC=example,DC=local` |
| `AD_BIND_DN` oder `Ad__BindDn` | Bind-DN fuer LDAP | `CN=ldap-reader,OU=Service Accounts,DC=example,DC=local` |
| `AD_BIND_PASSWORD` oder `Ad__BindPassword` | Passwort fuer LDAP-Bind | `change-me` |

## Lokal starten

```powershell
dotnet run
```

Danach im Browser:

```text
http://localhost:5000
```

Der konkrete Entwicklungsport steht in `Properties/launchSettings.json`.

## Docker lokal

```powershell
copy .env.example .env
docker compose up --build
```

Danach:

```text
http://localhost:3003
```

## Portainer Stack

In Portainer `portainer-stack.yml` als Stack verwenden und die Environment-Werte passend setzen.

```yaml
services:
  ad-group-user-compare:
    image: ghcr.io/immer-gut/ad-group-user-compare:latest
    restart: unless-stopped
    ports:
      - "${AD_GROUP_USER_COMPARE_PORT:-3003}:8080"
    environment:
      AD_LDAP_SERVER: "dc01.example.local"
      AD_LDAP_PORT: "389"
      AD_USE_SSL: "false"
      AD_SEARCH_BASE: "OU=Groups,DC=example,DC=local"
      AD_BIND_DN: "CN=ldap-reader,OU=Service Accounts,DC=example,DC=local"
      AD_BIND_PASSWORD: "change-me"
```

## AD-/LDAP-Hinweise

- Linux-Container koennen das Windows-`ActiveDirectory`-PowerShell-Modul nicht verwenden.
- Der Port nutzt `System.DirectoryServices.Protocols` und spricht LDAP direkt.
- In den meisten Umgebungen ist ein eigener LDAP-Lesebenutzer sinnvoll.
- Fuer LDAPS `AD_USE_SSL=true` und meistens `AD_LDAP_PORT=636` setzen.
- Der Button `LDAP testen` prueft die Verbindung schrittweise und zeigt konkrete Fehler fuer Bind, SearchBase oder Gruppenmuster.
- Der Vergleich betrachtet nur das aktuell geladene Ergebnis, nicht alle Gruppen eines Benutzers im gesamten AD.

## Entwicklung

```powershell
dotnet build
```

Die Browserdateien liegen unter `wwwroot`, die LDAP-Logik unter `Services/LdapAdGroupLookupService.cs`.
