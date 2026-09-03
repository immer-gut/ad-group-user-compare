# Project Notes

## Ziel

AD Group User Compare ist eine Docker-faehige Web-App fuer Ubuntu/Portainer. Sie liest Active-Directory-Gruppen per LDAP aus und macht Benutzerlisten sowie Gruppenvergleiche im Browser verfuegbar.

## Entscheidungen

- **ASP.NET Core statt WPF:** Die App soll im Linux-Container laufen und ueber den Browser bedient werden.
- **LDAP statt PowerShell/RSAT:** `System.DirectoryServices.Protocols` ersetzt das Windows-only `ActiveDirectory`-Modul.
- **Vergleich aus geladenen Daten:** Der Benutzervergleich arbeitet weiterhin nur auf dem geladenen Ergebnis.
- **Portainer-ready Defaults:** `portainer-stack.yml`, `docker-compose.yml` und `.env.example` nutzen denselben Standard-Port `3003`.
- **Laufzeitkonfiguration:** Der LDAP-Testdialog kann LDAP-Werte testen und in `/app/data/ad-settings.json` speichern. Gespeicherte Werte haben Vorrang vor Portainer-Environment-Defaults.
- **Zertifikatspruefung:** LDAPS/StartTLS prueft Server-Zertifikate standardmaessig. Fuer Diagnosefaelle kann die Pruefung im Testdialog oder per `AD_VERIFY_CERTIFICATE=false` deaktiviert werden.
- **Interne CA:** Optional kann `AD_CA_CERT_PATH` auf eine gemountete CA-Datei zeigen; der Container importiert sie beim Start in den Linux-Truststore und setzt OpenLDAP-TLS-Defaults.
- **Ticketsystem-Muster:** `ldap://`- und `ldaps://`-Server-URLs werden wie in der funktionierenden `ldap3`-Anbindung auf Host, Port und TLS-Modus normalisiert; StartTLS erfolgt vor dem Bind.
- **Versionsanzeige:** Die Website liest die Assembly-Version ueber `GET /api/version`; die Versionsquelle ist `AdGroupUserCompare.csproj`.
- **Keine Secrets im Repo:** LDAP-Passwoerter gehoeren in Portainer-Environment-Variablen oder lokale `.env`, nicht in Git. Interne Zertifikats- und Schluesseldateien werden ebenfalls ignoriert und nur zur Laufzeit gemountet.

## Grenzen

- Es ist kein vollstaendiges AD-Reporting-System.
- Kerberos/Integrated Windows Auth ist im Linux-Container nicht das Standardmodell; empfohlen ist LDAP Simple Bind ueber einen Lesebenutzer via LDAPS oder StartTLS.
- Windows Server 2025 bzw. gehaertete Domain Controller koennen Simple Bind ohne TLS mit `Strong authentication is required` ablehnen.
- LDAPS auf Port 389 ist eine Fehlkonfiguration; der Dialog korrigiert auf 636 und die API bricht mit einer klaren Meldung ab.
- Der LDAP-Test prueft DNS, TCP-Port und bei LDAPS den TLS-Handshake mit Zertifikatsdetails vor dem Bind, damit Container-Netzwerk- und TLS-Probleme frueh sichtbar werden.
- `RemoteCertificateChainErrors` bedeutet in der Regel, dass die interne AD-CA im Container nicht vertraut ist.
- Bei deaktivierter Zertifikatspruefung muss auch OpenLDAP/libldap `TLS_REQCERT=never` sehen, sonst kann `LdapConnection.Bind` trotz erfolgreichem eigenem TLS-Test mit `ErrorCode=81` abbrechen.
- Deaktivierte Zertifikatspruefung erleichtert Tests mit internen oder selbstsignierten Zertifikaten, reduziert aber die Sicherheit der TLS-Verbindung.
- Der Vergleich betrachtet nur die aktuell geladene Ergebnismenge.
- Reale AD-Performance haengt von Gruppenverschachtelung, LDAP-Indexen, Netzwerk und Berechtigungen ab.
- Gespeicherte LDAP-Passwoerter liegen im Docker-Volume als Laufzeitkonfiguration. Das Volume muss entsprechend geschuetzt werden.

## Datenschutz

Dokumentation und Beispiele verwenden nur generische Werte wie `example.local`, `dc01.example.local`, `abc*_1a*` und `ldap-reader`.
