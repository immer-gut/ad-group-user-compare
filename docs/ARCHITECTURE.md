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
Active Directory via LDAP/LDAPS
```

## Komponenten

- `Program.cs`: API-Endpunkte, Konfiguration und Static-File-Hosting.
- `wwwroot/`: Browseroberflaeche, Filter, CSV-Export und Uservergleich-Interaktion.
- `Models/`: DTOs fuer Suche, Ergebnisse und Vergleich.
- `Services/LdapAdGroupLookupService.cs`: LDAP-Suche, Gruppenauflistung, rekursive Member-Aufloesung und User-Attribut-Mapping.
- `Services/ResultComparisonService.cs`: Vergleich zweier Benutzer innerhalb des geladenen Ergebnisses.

## LDAP-Ablauf

1. Gruppen werden per LDAP-Filter `(&(objectClass=group)(name=<pattern>))` unterhalb der `SearchBase` gesucht.
2. Jede Gruppe wird rekursiv ueber das `member`-Attribut aufgeloest.
3. Verschachtelte Gruppen werden mit `visitedGroups` gegen Zyklen geschuetzt.
4. Benutzerattribute werden per Base-Search gelesen.
5. `userAccountControl` bestimmt, ob ein Benutzer aktiv ist.

## Deployment

Der Container lauscht intern auf Port `8080`. Portainer mappt standardmaessig Host-Port `3003`.
