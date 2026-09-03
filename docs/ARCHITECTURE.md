# Architecture

## Ueberblick

```text
Browser UI
  |
  v
ASP.NET Core Minimal API
  |
  v
LdapAdGroupLookupService
  |
  v
Active Directory via LDAPS or LDAP+StartTLS
```

## Komponenten

- `Program.cs`: API-Endpunkte, Konfiguration und Static-File-Hosting.
- `GET /api/version`: Liefert die in der Assembly hinterlegte Anwendungsversion fuer den Kopfbereich der Website.
- `wwwroot/`: Browseroberflaeche, Filter, CSV-Export und Uservergleich-Interaktion.
- `Models/`: DTOs fuer Suche, Ergebnisse und Vergleich.
- `Services/LdapAdGroupLookupService.cs`: LDAP-Suche, TLS/LDAPS-Verbindungsaufbau inklusive optionaler Zertifikatspruefung, Gruppenauflistung, rekursive Member-Aufloesung und User-Attribut-Mapping.
- `Services/LdapDiagnosticService.cs`: Schrittweiser LDAP-Test fuer DNS, TCP, TLS, Bind, SearchBase und Gruppenmuster.
- `Services/LdapEndpointResolver.cs`: Normalisiert Host-Eingaben sowie `ldap://`-/`ldaps://`-URLs in Server, Port und TLS-Modus.
- `Services/ManagedLdapClient.cs`: Kapselt den plattformunabhaengigen LDAP-Client, TLS, Bind und Paging fuer Linux/Docker.
- `Services/LdapSettingsStore.cs`: Laufzeitkonfiguration aus Environment-Defaults plus gespeicherten Dialogwerten.
- `Services/ResultComparisonService.cs`: Vergleich zweier Benutzer innerhalb des geladenen Ergebnisses.
- `docker-entrypoint.sh`: Importiert optional die interne CA vor dem App-Start in den System-Truststore.

## LDAP-Ablauf

1. Gruppen werden per LDAP-Filter `(&(objectClass=group)(cn=<pattern>))` unterhalb der `SearchBase` gesucht.
2. Jede Gruppe wird rekursiv ueber das `member`-Attribut aufgeloest.
3. Verschachtelte Gruppen werden mit `visitedGroups` gegen Zyklen geschuetzt.
4. Benutzerattribute werden per Base-Search gelesen.
5. `userAccountControl` bestimmt, ob ein Benutzer aktiv ist.

Der Bind nutzt fuer gehaertete Domain Controller standardmaessig LDAPS. Alternativ kann StartTLS auf Port 389 aktiviert werden. Server-Zertifikate werden standardmaessig geprueft; fuer Diagnose oder interne Testnetze kann die Pruefung deaktiviert werden.
Die Verbindungsreihenfolge entspricht dem bewaehrten Ticketsystem-Muster: bei StartTLS erst Verbindung oeffnen und TLS starten, danach explizit binden; bei LDAPS wird direkt die TLS-Verbindung aufgebaut und gebunden.

## Deployment

Der Container lauscht intern auf Port `8080`. Portainer mappt standardmaessig Host-Port `3003`.
LDAP-Dialogwerte werden unter `/app/data/ad-settings.json` gespeichert; der Stack bindet dafuer ein Docker-Volume ein.
Eine per `AD_CA_CERT_PATH` gemountete CA wird beim Start in den System-Truststore importiert. Der verwaltete TLS-Client nutzt diesen Truststore; bei deaktivierter Zertifikatspruefung greift sein eigener Validierungs-Callback.
