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
- sichtbare Anwendungsversion im Kopfbereich
- Docker- und Portainer-Stack fuer Ubuntu/Linux

## Konfiguration

Die App liest Konfiguration aus `appsettings.json`, .NET-Environment-Variablen und klassischen `AD_*`-Variablen.
Werte, die im LDAP-Testdialog gespeichert werden, liegen im Container-Volume unter `/app/data/ad-settings.json` und haben Vorrang vor den Environment-Defaults.

| Variable | Beschreibung | Beispiel |
| --- | --- | --- |
| `AD_GROUP_USER_COMPARE_PORT` | Host-Port fuer Docker/Portainer | `3003` |
| `AD_SETTINGS_PATH` oder `Ad__SettingsPath` | Speicherpfad fuer LDAP-Dialogwerte | `/app/data/ad-settings.json` |
| `AD_LDAP_SERVER` oder `Ad__Server` | Domain Controller, LDAP-Host oder URL | `ldaps://dc01.example.local:636` |
| `AD_LDAP_PORT` oder `Ad__Port` | LDAP-Port | `636` fuer LDAPS, `389` fuer StartTLS/LDAP |
| `AD_USE_SSL` oder `Ad__UseSsl` | LDAPS aktivieren | `true` |
| `AD_USE_START_TLS` oder `Ad__UseStartTls` | StartTLS auf Port 389 aktivieren | `false` |
| `AD_VERIFY_CERTIFICATE` oder `Ad__VerifyCertificate` | Server-Zertifikat bei LDAPS/StartTLS pruefen | `true` |
| `AD_CA_CERT_PATH` | Optionaler Pfad zu einer gemounteten internen CA-Datei, die beim Containerstart im System-Truststore hinterlegt wird | `/run/certs/ad-ca.crt` |
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

Optional fuer eine interne AD-CA:

```yaml
    volumes:
      - ad-group-user-compare-data:/app/data
      - /opt/ad-group-user-compare/ad-ca.crt:/run/certs/ad-ca.crt:ro
    environment:
      AD_CA_CERT_PATH: "/run/certs/ad-ca.crt"
```

## AD-/LDAP-Hinweise

- Linux-Container koennen das Windows-`ActiveDirectory`-PowerShell-Modul nicht verwenden.
- Der Port nutzt den verwalteten `Novell.Directory.Ldap.NETStandard`-Client und spricht LDAP direkt.
- In den meisten Umgebungen ist ein eigener LDAP-Lesebenutzer sinnvoll.
- Im Dialog `LDAP testen` koennen Server, Port, SSL/StartTLS, Zertifikatspruefung, SearchBase, Gruppenmuster, Bind-DN, Bind-Passwort und Paging getestet und gespeichert werden.
- Wie im Ticketsystem kann der Server als `ldaps://host:636` oder `ldap://host:389` eingegeben werden. Schema und URL-Port haben Vorrang und werden beim Speichern in Host, Port und TLS-Modus normalisiert.
- Portainer-Environment-Werte muessen nicht geloescht werden. Sie bleiben Start-/Fallbackwerte, gespeicherte Dialogwerte haben danach Vorrang.
- Das Bind-Passwort wird nicht im Browser angezeigt. Beim Speichern bleibt ein vorhandenes gespeichertes Passwort erhalten, wenn das Passwortfeld leer bleibt.
- Wenn kein Passwort gespeichert ist und das Passwortfeld leer bleibt, kann weiterhin `AD_BIND_PASSWORD` aus Portainer als Fallback genutzt werden.
- Wer alle LDAP-Werte komplett ohne Portainer-Environment verwalten will, traegt das Passwort einmal im Dialog ein und speichert es.
- Windows Server 2025 und gehaertete Domain Controller koennen unverschluesselten Simple Bind mit `Strong authentication is required` ablehnen.
- Empfohlen ist LDAPS mit `AD_USE_SSL=true` und `AD_LDAP_PORT=636`.
- Alternativ kann StartTLS auf Port 389 genutzt werden: `AD_USE_SSL=false`, `AD_USE_START_TLS=true`, `AD_LDAP_PORT=389`.
- `LDAPS / SSL` zusammen mit Port `389` ist normalerweise falsch. Der Dialog korrigiert das auf Port `636`; die API meldet diese Kombination als Konfigurationsfehler.
- Der Container muss dem Zertifikat des Domain Controllers bzw. der internen CA vertrauen, sonst schlaegt LDAPS/StartTLS beim TLS-Aufbau fehl.
- Bei `RemoteCertificateChainErrors` fehlt dem Container normalerweise die interne Root- oder Issuing-CA. Exportiere die CA als Base-64-codierte X.509-Datei und mounte sie z. B. nach `/run/certs/ad-ca.crt`; setze dann `AD_CA_CERT_PATH=/run/certs/ad-ca.crt`.
- Falls die interne CA im Container noch nicht vertraut ist, kann die Zertifikatspruefung im Testdialog oder mit `AD_VERIFY_CERTIFICATE=false` deaktiviert werden. Der verwaltete TLS-Client akzeptiert das Serverzertifikat dann wie das Ticketsystem mit deaktivierter Pruefung; das sollte nur zur Diagnose oder in kontrollierten internen Netzen genutzt werden.
- StartTLS folgt exakt der Reihenfolge des Ticketsystems: Verbindung auf Port 389 oeffnen, StartTLS aushandeln und erst danach mit dem Lesebenutzer binden.
- Der Button `LDAP testen` prueft die Verbindung schrittweise inklusive DNS-Aufloesung, TCP-Port, LDAPS-TLS-Handshake mit Zertifikatsdetails, Bind, SearchBase und Gruppenmuster.
- Active Directory kann bei einer Unterbaum-Suche zusaetzliche LDAP-Referrals liefern. Da Referral-Following deaktiviert ist, ueberspringt die App diese Verweise und verarbeitet lokale Treffer weiter; der LDAP-Test zeigt die Anzahl uebersprungener Referrals an.
- Liegen die gesuchten Gruppen ausschliesslich in einer anderen AD-Domaene, muessen LDAP-Server und SearchBase auf den zustaendigen Namensbereich zeigen. Die App sendet Bind-Zugangsdaten nicht automatisch an Referral-Ziele.
- Wenn `Gruppenmuster ohne Paging` funktioniert, aber `Gruppenmuster mit Paging` fehlschlaegt, kann `AD_USE_PAGING=false` als Workaround gesetzt werden.
- Der Vergleich betrachtet nur das aktuell geladene Ergebnis, nicht alle Gruppen eines Benutzers im gesamten AD.

## Entwicklung

```powershell
dotnet build
```

Die Browserdateien liegen unter `wwwroot`, die LDAP-Logik unter `Services/LdapAdGroupLookupService.cs`.
Die im Kopfbereich angezeigte Version stammt aus `AdGroupUserCompare.csproj` und wird ueber `GET /api/version` geladen.

## Projektdokumentation

- [Architektur](docs/ARCHITECTURE.md)
- [Projektentscheidungen und Grenzen](docs/PROJECT_NOTES.md)
