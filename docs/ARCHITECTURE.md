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
- `wwwroot/`: Browseroberflaeche, Filter, CSV-Export und Uservergleich-Interaktion.
- `Models/`: DTOs fuer Suche, Ergebnisse und Vergleich.
- `Services/LdapAdGroupLookupService.cs`: LDAP-Suche, TLS/LDAPS-Verbindungsaufbau inklusive optionaler Zertifikatspruefung, Gruppenauflistung, rekursive Member-Aufloesung und User-Attribut-Mapping.
- `Services/LdapDiagnosticService.cs`: Schrittweiser LDAP-Test fuer DNS, TCP, TLS, Bind, SearchBase und Gruppenmuster.
- `Services/NativeLdapTlsOptions.cs`: Uebergibt Zertifikatspruefung und CA-Trust an die native OpenLDAP-Bibliothek.
- `Services/LdapSettingsStore.cs`: Laufzeitkonfiguration aus Environment-Defaults plus gespeicherten Dialogwerten.
- `Services/ResultComparisonService.cs`: Vergleich zweier Benutzer innerhalb des geladenen Ergebnisses.
- `docker-entrypoint.sh`: Importiert optional die interne CA und erzeugt die OpenLDAP-TLS-Konfiguration vor dem App-Start.

## LDAP-Ablauf

1. Gruppen werden per LDAP-Filter `(&(objectClass=group)(name=<pattern>))` unterhalb der `SearchBase` gesucht.
2. Jede Gruppe wird rekursiv ueber das `member`-Attribut aufgeloest.
3. Verschachtelte Gruppen werden mit `visitedGroups` gegen Zyklen geschuetzt.
4. Benutzerattribute werden per Base-Search gelesen.
5. `userAccountControl` bestimmt, ob ein Benutzer aktiv ist.

Der Bind nutzt fuer gehaertete Domain Controller standardmaessig LDAPS. Alternativ kann StartTLS auf Port 389 aktiviert werden. Server-Zertifikate werden standardmaessig geprueft; fuer Diagnose oder interne Testnetze kann die Pruefung deaktiviert werden.

## Deployment

Der Container lauscht intern auf Port `8080`. Portainer mappt standardmaessig Host-Port `3003`.
LDAP-Dialogwerte werden unter `/app/data/ad-settings.json` gespeichert; der Stack bindet dafuer ein Docker-Volume ein.
Eine per `AD_CA_CERT_PATH` gemountete CA wird beim Start in den System-Truststore importiert. `TLS_CACERT` verweist auf diesen Truststore; `TLS_REQCERT` folgt der wirksamen Zertifikatspruefung.
