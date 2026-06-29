# Project Notes

## Ziel

AD Group User Compare ist eine Docker-faehige Web-App fuer Ubuntu/Portainer. Sie liest Active-Directory-Gruppen per LDAP aus und macht Benutzerlisten sowie Gruppenvergleiche im Browser verfuegbar.

## Entscheidungen

- **ASP.NET Core statt WPF:** Die App soll im Linux-Container laufen und ueber den Browser bedient werden.
- **LDAP statt PowerShell/RSAT:** `System.DirectoryServices.Protocols` ersetzt das Windows-only `ActiveDirectory`-Modul.
- **Vergleich aus geladenen Daten:** Der Benutzervergleich arbeitet weiterhin nur auf dem geladenen Ergebnis.
- **Portainer-ready Defaults:** `portainer-stack.yml`, `docker-compose.yml` und `.env.example` nutzen denselben Standard-Port `3003`.
- **Keine Secrets im Repo:** LDAP-Passwoerter gehoeren in Portainer-Environment-Variablen oder lokale `.env`, nicht in Git.

## Grenzen

- Es ist kein vollstaendiges AD-Reporting-System.
- Kerberos/Integrated Windows Auth ist im Linux-Container nicht das Standardmodell; empfohlen ist LDAP Simple Bind ueber einen Lesebenutzer, idealerweise via LDAPS.
- Der Vergleich betrachtet nur die aktuell geladene Ergebnismenge.
- Reale AD-Performance haengt von Gruppenverschachtelung, LDAP-Indexen, Netzwerk und Berechtigungen ab.

## Datenschutz

Dokumentation und Beispiele verwenden nur generische Werte wie `example.local`, `dc01.example.local`, `abc*_1a*` und `ldap-reader`.
