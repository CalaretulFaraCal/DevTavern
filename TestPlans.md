# Test Plans - GitHub Chat (DevTavern)

Acest document reprezintă planul de testare (Test Plans) impus la **Cerința 4**, pe lângă testele automate (Suite) din proiectul xUnit.

## 1. Testarea Autentificării (Sistemul OAuth)
| ID Test | Acțiune Utilizator | Comportament Așteptat | Stare |
|---------|-------------------|-----------------------|-------|
| T-001   | Apăsare "Login with GitHub" din client | Redirecționare spre site-ul github.com pentru autorizare. | Neînceput |
| T-002   | Autorizare cu succes din Github | GitHub ne trimite un token, iar server-ul salvează/actualizează datele Userului în tabela Users. | Neînceput |
| T-003   | Autorizare refuzată | Tokenul nu se trimite, apare mesaj de eroare prietenos în UI. | Neînceput |

## 2. Testarea Generării Spatiilor de Lucru (Factory Pattern)
| ID Test | Acțiune Utilizator | Comportament Așteptat | Stare |
|---------|-------------------|-----------------------|-------|
| T-004   | Sincronizare repo după prima logare | API-ul apelează Factory Class pentru a genera exact 2 canale (Project și Off-Topic) pentru noile repository-uri găsite pe GitHub. | Neînceput |
| T-005   | Accesarea listei de canale din UI | Canalele nou generate sunt vizibile corect sub numele repository-ului aferent. | Neînceput |

## 3. Testarea Comunicării (SignalR WebSockets)
| ID Test | Acțiune Utilizator | Comportament Așteptat | Stare |
|---------|-------------------|-----------------------|-------|
| T-006   | User A trimite un mesaj text în canalul X | Mesajul se salvează în SQL, iar apoi User B (din același canal) îl primește pe ecran instant fără refresh. | Neînceput |
| T-007   | Trimitere mesaj gol | Trimiterea se blochează din front-end; nu se face apel REST API/Socket. | Neînceput |
