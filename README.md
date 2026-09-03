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
Werte, die im LDAP-Testdialog gespeichert werden, liegen im Container-Volume unter `/app/data/ad-settings.json` und haben Vorrang vor den Environment-Defaults.

| Variable | Beschreibung | Beispiel |
| --- | --- | --- |
| `AD_GROUP_USER_COMPARE_PORT` | Host-Port fuer Docker/Portainer | `3003` |
| `AD_SETTINGS_PATH` oder `Ad__SettingsPath` | Speicherpfad fuer LDAP-Dialogwerte | `/app/data/ad-settings.json` |
| `AD_LDAP_SERVER` oder `Ad__Server` | Domain Controller oder LDAP-Host | `dc01.example.local` |
| `AD_LDAP_PORT` oder `Ad__Port` | LDAP-Port | `636` fuer LDAPS, `389` fuer StartTLS/LDAP |
| `AD_USE_SSL` oder `Ad__UseSsl` | LDAPS aktivieren | `true` |
| `AD_USE_START_TLS` oder `Ad__UseStartTls` | StartTLS auf Port 389 aktivieren | `false` |
| `AD_VERIFY_CERTIFICATE` oder `Ad__VerifyCertificate` | Server-Zertifikat bei LDAPS/StartTLS pruefen | `true` |
| `AD_SEARCH_BASE` oder `Ad__SearchBase` | Basis-DN fuer Gruppensuche | `OU=Groups,DC=example,DC=local` |
| `AD_BIND_DN` oder `Ad__BindDn` | Bind-DN fuer LDAP | `CN=ldap-reader,OU=Service Accounts,DC=example,DC=local` |
| `AD_BIND_PASSWORD` oder `Ad__BindPassword` | Passwort fuer LDAP-Bind | `change-me` |
| `AD_USE_PAGING` oder `Ad__UsePaging` | LDAP PageResult-Control fuer Gruppensuche nutzen | `true` |

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
    volumes:
      - ad-group-user-compare-data:/app/data
    environment:
      AD_SETTINGS_PATH: "/app/data/ad-settings.json"
      AD_LDAP_SERVER: "dc01.example.local"
      AD_LDAP_PORT: "636"
      AD_USE_SSL: "true"
      AD_USE_START_TLS: "false"
      AD_VERIFY_CERTIFICATE: "true"
      AD_SEARCH_BASE: "OU=Groups,DC=example,DC=local"
      AD_BIND_DN: "CN=ldap-reader,OU=Service Accounts,DC=example,DC=local"
      AD_BIND_PASSWORD: "change-me"
      AD_USE_PAGING: "true"

volumes:
  ad-group-user-compare-data:
```

## AD-/LDAP-Hinweise

- Linux-Container koennen das Windows-`ActiveDirectory`-PowerShell-Modul nicht verwenden.
- Der Port nutzt `System.DirectoryServices.Protocols` und spricht LDAP direkt.
- In den meisten Umgebungen ist ein eigener LDAP-Lesebenutzer sinnvoll.
- Im Dialog `LDAP testen` koennen Server, Port, SSL/StartTLS, Zertifikatspruefung, SearchBase, Gruppenmuster, Bind-DN, Bind-Passwort und Paging getestet und gespeichert werden.
- Portainer-Environment-Werte muessen nicht geloescht werden. Sie bleiben Start-/Fallbackwerte, gespeicherte Dialogwerte haben danach Vorrang.
- Das Bind-Passwort wird nicht im Browser angezeigt. Beim Speichern bleibt ein vorhandenes gespeichertes Passwort erhalten, wenn das Passwortfeld leer bleibt.
- Wenn kein Passwort gespeichert ist und das Passwortfeld leer bleibt, kann weiterhin `AD_BIND_PASSWORD` aus Portainer als Fallback genutzt werden.
- Wer alle LDAP-Werte komplett ohne Portainer-Environment verwalten will, traegt das Passwort einmal im Dialog ein und speichert es.
- Windows Server 2025 und gehaertete Domain Controller koennen unverschluesselten Simple Bind mit `Strong authentication is required` ablehnen.
- Empfohlen ist LDAPS mit `AD_USE_SSL=true` und `AD_LDAP_PORT=636`.
- Alternativ kann StartTLS auf Port 389 genutzt werden: `AD_USE_SSL=false`, `AD_USE_START_TLS=true`, `AD_LDAP_PORT=389`.
- `LDAPS / SSL` zusammen mit Port `389` ist normalerweise falsch. Der Dialog korrigiert das auf Port `636`; die API meldet diese Kombination als Konfigurationsfehler.
- Der Container muss dem Zertifikat des Domain Controllers bzw. der internen CA vertrauen, sonst schlaegt LDAPS/StartTLS beim TLS-Aufbau fehl.
- Falls die interne CA im Container noch nicht vertraut ist, kann die Zertifikatspruefung im Testdialog oder mit `AD_VERIFY_CERTIFICATE=false` deaktiviert werden. Das sollte nur zur Diagnose oder in kontrollierten internen Netzen genutzt werden.
- Der Button `LDAP testen` prueft die Verbindung schrittweise inklusive DNS-Aufloesung, TCP-Port, Bind, SearchBase und Gruppenmuster.
- Wenn `Gruppenmuster ohne Paging` funktioniert, aber `Gruppenmuster mit Paging` fehlschlaegt, kann `AD_USE_PAGING=false` als Workaround gesetzt werden.
- Der Vergleich betrachtet nur das aktuell geladene Ergebnis, nicht alle Gruppen eines Benutzers im gesamten AD.

## Entwicklung

```powershell
dotnet build
```

Die Browserdateien liegen unter `wwwroot`, die LDAP-Logik unter `Services/LdapAdGroupLookupService.cs`.
