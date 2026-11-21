# MiniGTA – gra w stylu GTA w Unity (z multiplayerem)

Prototyp gry z otwartym światem: chodzenie, strzelanie, kradzież i jazda autami, ruch uliczny,
przechodnie, policja z systemem gwiazdek (1–5), misje (także w co-opie) i multiplayer przez sieć.
Grafika to na razie proste bryły: pod spodem są działające mechaniki, na które podmieniasz modele.

- **Silnik:** Unity 6 (6000.2.2f1)
- **Sieć:** Netcode for GameObjects 2.13 + Unity Transport
- **AI:** AI Navigation (NavMesh) 2.0

---

## 1. Szybki start

1. Otwórz folder `C:\Users\kacpe\UnityProjects\MiniGTA` w **Unity Hub → Add → Add project from disk**.
2. Otwórz scenę `Assets/GTA/Scenes/City.unity`.
3. Wciśnij **Play**, a potem **HOSTUJ GRĘ**.

Gotowa wersja .exe jest w `Builds/MiniGTA.exe`.

Chcesz od nowa wygenerować miasto i prefaby (np. po zmianach w generatorze)? Użyj menu
**MiniGTA → 1. Zbuduj wszystko**. Uwaga: to nadpisuje scenę `City.unity` i prefaby w `Generated/`.

### Sterowanie

| Klawisz | Akcja |
|---|---|
| WASD | ruch / jazda |
| Shift | sprint |
| Spacja | skok / hamulec ręczny |
| Mysz | kamera |
| LPM / PPM | strzał / celowanie |
| 1–9, kółko myszy | wybór broni (8 = granat, 9 = RPG) |
| E | menu sklepu / baru / kryjówki (stojąc w kolorowym kręgu) |
| PPM na kasjera (z bronią) | napad na sklep 24/7 |
| M | duża mapa |
| R | przeładowanie / postawienie przewróconego auta |
| F | wsiądź / wysiądź (kradzież auta) |
| G | syrena (w radiowozie) |
| Esc | odblokowanie kursora i przycisk „Rozłącz” |
| F1 | pokaż / ukryj pomoc |
| **Kody:** F5 / F6 / F7 / F8 / F9 | +1 gwiazdka / +$1000 / zdrowie + wszystkie bronie / auto obok / wyczyść gwiazdki |

---

## 2. Multiplayer: jak grać razem

| Sytuacja | Co zrobić |
|---|---|
| **Dwa okna na jednym PC** | Zbuduj grę (**MiniGTA → 2. Zbuduj grę**). W jednym oknie *Hostuj*, w drugim *Dołącz* z IP `127.0.0.1`. Możesz też grać w edytorze (Play) + .exe. |
| **Sieć lokalna (LAN)** | Host podaje swój adres IP (`ipconfig` → IPv4, np. `192.168.1.15`). Pozostali wpisują go w polu „Adres IP” i klikają *Dołącz*. Zezwól grze w zaporze Windows. |
| **Przez internet** | **A)** Na routerze hosta przekieruj port **7777 UDP** (port forwarding), a znajomi wpisują Twoje publiczne IP. **B)** Unity Relay, bez otwierania portów – patrz rozdział 5.7. **C)** Wirtualny LAN (Radmin VPN, ZeroTier) i IP z tej sieci. |
| **Serwer dedykowany** | `MiniGTA.exe -batchmode -nographics -server -port 7777` |

Parametry linii poleceń: `-host`, `-client -ip 1.2.3.4`, `-server`, `-port 7777`, `-name Nick`.

### Co jest zsynchronizowane

- **Gracz:** pozycję wysyła właściciel (`ClientNetworkTransform`), więc sterowanie jest płynne. Zdrowie, kasę, gwiazdki i misje trzyma **serwer**.
- **Strzały:** klient wysyła tylko „strzelam stąd w tę stronę” (`FireRpc`). Raycast i obrażenia liczy serwer.
- **Auta:** fizykę liczy właściciel. Gdy wsiadasz, serwer przekazuje Ci auto (`ChangeOwnership`), a po wyjściu odbiera je z powrotem.
- **NPC / policja / ruch uliczny:** AI działa wyłącznie na serwerze, klienci tylko to wyświetlają.
- **Misje:** wszyscy gracze stojący blisko znacznika (12 m) w momencie startu robią misję razem i wszyscy dostają nagrodę.

---

## 3. Mapa projektu: co jest gdzie

```
Assets/GTA/
├── Scripts/
│   ├── Network/   ClientNetworkTransform, NetworkUI (menu Host/Dołącz), GameUtil (pomocnicze)
│   ├── Player/    PlayerController (ruch, śmierć, WASTED), PlayerCamera (TPP), GameInput (KLAWISZE),
│   │              PlayerStats (nick, $), WantedSystem (gwiazdki), VehicleInteractor (F – wsiadanie),
│   │              SpawnPoint (szpitale), DebugCheats (kody F5–F9)
│   ├── Combat/    Health (HP wszystkiego), WeaponController (bronie), WeaponVisual (model w ręce),
│   │              Projectile + CombatUtil (granat, rakieta, wybuchy), Effects (smugi, wybuchy)
│   ├── Shops/     ShopZone (strefa sklepu na mapie), ShopClient (kupowanie, warsztat)
│   ├── Vehicles/  Vehicle (fizyka auta + AI jazdy), WaypointPath (trasy ruchu), PoliceCarAI (radiowozy)
│   ├── AI/        PedestrianAI (przechodnie), CombatNpcAI (policja i gangi)
│   ├── Spawning/  PedestrianSpawner, TrafficSpawner, PoliceSpawner, PickupSpawner, Pickup
│   ├── Missions/  Mission (definicja misji), MissionManager (logika), PlayerMission (stan na HUD)
│   └── UI/        HUD (wszystko na ekranie + minimapa)
├── Editor/        GTASceneBuilder (generator miasta, build .exe), VehicleImporter (model → auto),
│                  PackIntegrator (podłącza paczki z Asset Store)
├── Weapons/       modele broni w ręce (View_*), prefaby granatu i rakiety
├── Vehicles/      auta zrobione z modeli z paczek
├── Generated/     wygenerowane prefaby, materiały, NavMesh, lista prefabów sieciowych
└── Scenes/City.unity
```

W scenie wszystkie systemy siedzą na obiekcie **GameManagers**: NetworkManager, menu, HUD,
spawnery i MissionManager. Ustawienia (liczba przechodniów, aut, policji itd.) zmieniasz w Inspectorze tego obiektu.

---

## 4. Najczęstsze zmiany

| Chcę… | Gdzie |
|---|---|
| zmienić klawisze / dodać pada | `GameInput.cs` – całe sterowanie jest w jednym pliku |
| więcej / mniej przechodniów | GameManagers → PedestrianSpawner → `maxPedestrians` |
| więcej aut na ulicach | GameManagers → TrafficSpawner → `maxTrafficCars` |
| ostrzejszą / łagodniejszą policję | GameManagers → PoliceSpawner → tabele `officersOnFoot`, `policeCars` (indeks = liczba gwiazdek). Celność i obrażenia: prefab `PoliceOfficer` → CombatNpcAI |
| szybciej / wolniej gubić gwiazdki | prefab `Player` → WantedSystem → `baseCooldown`, `cooldownPerStar` |
| za co są gwiazdki | szukaj `WantedSystem.ReportCrime(` – są w PedestrianAI (zabójstwo), CombatNpcAI (atak / zabicie policjanta, strzały przy policji) i Vehicle (kradzież auta: z kierowcą, radiowozu) |
| prowadzenie auta | prefab auta → Vehicle: `enginePower`, `maxSpeed`, `frontGrip`, `rearGrip` (mniejsza wartość = łatwiejszy drift) |
| kasę na start / karę za śmierć | Player → PlayerStats `startMoney`, PlayerController `deathMoneyPenalty` |

---

## 5. Jak dodać nowe rzeczy

### 5.0 Sklepy, bronie i paczki z Asset Store

**Podłączenie paczek:** otwórz scenę `City.unity` i kliknij **MiniGTA → 5. Podłącz modele z paczek (bronie + auta + sklepy)**, potem zapisz scenę (Ctrl+S).
Narzędzie obsługuje „Low Poly Weapons VOL.1” i „Super Retro Compact Pack”. Można je uruchamiać wielokrotnie.

**Bronie** (prefab `Player` → WeaponController → Weapons):

| Klawisz | Broń | Model | Cena |
|---|---|---|---|
| 1 | Pięści | – | – |
| 2 | Pistolet | M1911 | na start |
| 3 | Uzi | Uzi | $800 |
| 4 | Shotgun | Bennelli M4 | $1200 |
| 5 | AK-74 | AK74 | $2500 |
| 6 | M249 | M249 | $4000 |
| 7 | Snajperka (zoom pod PPM) | M107 | $5000 |
| 8 | Granat (rzut, wybuch po 3 s) | RGD-5 | $600 |
| 9 | RPG (wybuch przy uderzeniu) | RPG7 | $8000 |

Każda broń ma pola `price` (cena broni), `ammoPrice` (magazynek / sztuka granatu), `startsOwned` i `viewModel` (model w ręce).
Nowa broń: dodaj wpis na liście; jako `viewModel` przeciągnij prefab modelu z dzieckiem `Muzzle` (wylot lufy).
Najprościej zduplikować któryś `Assets/GTA/Weapons/View_*`.

**Sklepy** (obiekt `Shops` w scenie, komponent `ShopZone`):
- *Ammu-Nation* (zielony krąg, 2 sztuki): **E** otwiera menu, w którym kupujesz bronie, amunicję, granaty i apteczkę.
- *Pay'n'Spray* (niebieski krąg): wjedź autem → naprawa za $500 i **zgubienie gwiazdek**, o ile policja cię akurat nie widzi.
- Nowy sklep: pusty obiekt + `ShopZone`, wybierz typ i promień. Zakupy sprawdza serwer (pieniądze, odległość), więc nie da się ich oszukać z klienta.

### 5.1 Nowa misja (bez kodu)

1. W scenie: **GameObject → Create Empty**, ustaw go tam, gdzie ma być start misji (żółty walec).
2. **Add Component → Mission**. Wypełnij nazwę, nagrodę i listę **Steps**.
3. Dla kroków potrzebujących miejsca stwórz puste obiekty (np. jako dzieci misji) i przeciągnij je w pole `Target`.

Dostępne typy kroków:

| Typ | Działanie |
|---|---|
| `GoTo` | dojdź lub dojedź do `Target` (promień `radius`) |
| `DriveTo` | dojedź **autem** do `Target`. Jeśli wcześniej był `spawnVehicleAtTarget`, musi to być tamto auto |
| `EnterVehicle` | wsiądź do auta. Z `spawnVehicleAtTarget` = auto pojawia się w `Target` |
| `KillEnemies` | przy `Target` pojawia się `enemyCount` gangsterów atakujących uczestników |
| `LoseWanted` | zgub policję. Z `setWantedOnStart = 3` krok zaczyna się od 3 gwiazdek |
| `Survive` | przetrwaj `duration` sekund |

Dodatkowo: `timeLimit` (przegrana po czasie), `repeatable`, `coopJoinRadius`.
Przykłady to obiekty `Mission_Kurier`, `Mission_Gang` i `Mission_Oblawa` w scenie.

**Nowy typ kroku** (np. „zbierz paczki”):
1. Dodaj wartość do enuma `MissionStepType` (`Mission.cs`).
2. W `MissionManager.cs` w `StartStep` przygotuj krok (zespawnuj rzeczy).
3. W `IsStepDone` zwróć `true`, gdy krok jest zaliczony.
4. Opcjonalnie w `UpdateObjective` ustaw znacznik celu.

### 5.2 Nowe auto

**Model z Asset Store (najszybciej):** zaimportuj paczkę (Package Manager → My Assets → Download → Import),
zaznacz prefab auta w oknie Project i wybierz **MiniGTA → 3. Zrób AUTO z zaznaczonego modelu** (albo **4. … RADIOWÓZ**), potem zapisz scenę.
Narzędzie usuwa fizykę i skrypty paczki, dopasowuje collider, rozpoznaje koła po nazwach i rejestruje auto w sieci oraz w ruchu ulicznym.
Jeśli auto jedzie tyłem, obróć obiekt `Model` w prefabie o 180°.

Ręcznie (własny model 3D):
1. Wrzuć model jako dziecko prefabu i usuń stare bryły (`Body`, `Cabin`).
2. Dopasuj BoxCollidery do karoserii.
3. Ustaw 4 obiekty `Wheel_*` w miejscach kół (kolejność: lewe przednie, prawe przednie, lewe tylne, prawe tylne). Jako `wheelVisuals` podepnij modele kół.
4. **Ważne:** dodaj prefab do listy `Generated/MiniGTANetworkPrefabs` (inaczej nie pojawi się u klientów).
5. Dodaj go do TrafficSpawner → `carPrefabs`.

Komponenty nie mogą być zdublowane: dokładnie jeden `Health` i jeden `NetworkObject`.

Motocykle, łodzie i helikoptery to nowa klasa na wzór `Vehicle.cs`. Kluczowe jest, żeby fizykę liczył tylko `IsOwner`, a wsiadanie przechodziło przez `SetDriverServer` / `ClearDriverServer`.

### 5.3 Nowa broń

Prefab `Player` → **WeaponController → Weapons → +**. Ustaw `damage`, `fireRate`, `range`,
`spread`, `pellets` (> 1 = śrutówka), `automatic` i `magazineSize`. Klawisz to numer pozycji na liście.

Pole `kind`: `Hitscan` (zwykła broń), `Melee`, `Throwable` (granat) albo `Rocket` (RPG). Granat i rakieta używają prefabów
`Projectile_Grenade` / `Projectile_Rocket` (pola `grenadePrefab`, `rocketPrefab`). Siłę wybuchu ustawiasz przez `damage` i `explosionRadius`.
Snajperka: `aimFov` = zoom przy celowaniu. Zobacz też punkt 5.0.

### 5.4 Nowy typ NPC

- **Przechodzień:** zduplikuj `Pedestrian_0`, zmień wygląd i dodaj do PedestrianSpawner → `pedestrianPrefabs`.
- **Wrogi gang / ochroniarze:** zduplikuj `Gangster`. CombatNpcAI atakuje graczy z listy `HostileTo` (misje dodają tam uczestników) oraz każdego, kto go zaatakuje.
- **Nowa frakcja:** dodaj wartość do enuma `Faction` (`Health.cs`) i obsłuż ją w `CombatNpcAI.PickTarget()`.
- Każdy nowy prefab sieciowy dodaj do `MiniGTANetworkPrefabs`.

### 5.5 Prawdziwe modele i animacje postaci

1. Pobierz postać i animacje (np. **Mixamo**: idle, walk, run, death, shoot) i zaimportuj jako *Humanoid*.
2. Stwórz Animator Controller z parametrami **`Speed`** (float), **`Dead`** (bool) i **`Shoot`** (trigger). Skrypty już je ustawiają.
3. W prefabie usuń kapsuły `Body`/`Face`/`Hat`, wstaw model jako dziecko i przeciągnij jego Animator w pole `animator` (PlayerController / PedestrianAI / CombatNpcAI).
4. Żeby inni gracze widzieli Twoje animacje, dodaj do prefabu gracza **NetworkAnimator**. Dla właściciela potrzebny jest wariant „owner authoritative”: klasa dziedzicząca po `NetworkAnimator` z `OnIsServerAuthoritative() => false`.
5. Ragdoll: **GameObject → 3D Object → Ragdoll…**. Włączaj go w `OnDeadChanged`.

### 5.6 Własna mapa

1. Zbuduj teren lub budynki (np. ProBuilder, Terrain, darmowe paczki miast).
2. Na obiekt-rodzica dodaj **NavMeshSurface** i kliknij **Bake**, żeby piesi i policja mogli chodzić.
3. Ruch uliczny: pusty obiekt + `WaypointPath`, a jako jego dzieci kolejne punkty wzdłuż pasa (pętla).
4. Dodaj kilka obiektów z `SpawnPoint` (miejsca odrodzenia).
5. Skopiuj obiekt **GameManagers** ze sceny City i ustaw sloty `parkedSpots` i `spots` znajdziek.

### 5.7 Internet bez przekierowania portów (Unity Relay)

1. Zainstaluj pakiet `com.unity.services.multiplayer` (Window → Package Manager).
2. Podłącz projekt do Unity Cloud (Edit → Project Settings → Services) i włącz Relay w dashboardzie.
3. W `NetworkUI.cs` przed `StartHost()` utwórz sesję (`MultiplayerService.Instance.CreateSessionAsync` z `.WithRelayNetwork()`), a klient dołącza kodem (`JoinSessionByCodeAsync`). Kod sesji pokaż hostowi w menu.

### 5.8 Dźwięki i efekty

Wszystkie efekty są w `Effects.cs` (`Tracer`, `Explosion`); wywołują je RPC na każdym kliencie.
Tam dodaj `AudioSource.PlayClipAtPoint(...)` i Particle Systemy. Silnik auta: AudioSource w prefabie auta,
z `pitch` zależnym od `Vehicle.SpeedKmh`.

### 5.9 Inne pomysły i gdzie je podpiąć

| Funkcja | Gdzie / jak |
|---|---|
| sklep z bronią, warsztat | strefa jak w `Pickup.cs` + `PlayerStats.TrySpend()` na serwerze |
| zapis gry | serwer zapisuje `Money` i ukończone misje (`MissionManager._completed`) do JSON lub Unity Cloud Save |
| czat | `[Rpc(SendTo.Server)] SendChatRpc(string)` → `[Rpc(SendTo.ClientsAndHost)]` do wszystkich, wyświetl w HUD.Notify |
| strzelanie z auta | w `WeaponController.Update` nie przerywaj, gdy `InVehicle`, tylko ogranicz kąt |
| cykl dnia i nocy | obracaj Directional Light. Czas trzymaj w `NetworkVariable<float>` na serwerze |
| ładniejsze UI | przepisz `HUD.cs` na Canvas/UI Toolkit; dane bierz z tych samych pól (`PlayerController.Local`) |

---

### 5.10 Miasto, wnętrza i ruch uliczny

**Mapa** (generuje **MiniGTA → 1. Zbuduj wszystko**, plik `Editor/GTASceneBuilder.City.cs`):
- 10×10 kwartałów (~600 m) + obwodnica (autostrada) z 12 zjazdami.
- Dzielnice: śródmieście (wieżowce), centrum, przedmieścia (domki, trawniki, drzewa), strefa przemysłowa (magazyny, kontenery), park z fontanną, stunt park ze skoczniami.
- Układ zmieniasz w `DistrictOf()` (która dzielnica gdzie) i tablicy `Interiors` (które budynki mają wnętrze).

**Budynki z wnętrzem**: parter jest pusty w środku, z rozsuwanymi drzwiami (`AutoDoor`), światłem i wyposażeniem. Wchodzi się normalnie, bez ładowania, więc policja i przechodnie też wchodzą. Typy (`ShopType`):

| Typ | Co robi |
|---|---|
| `Safehouse` (kryjówka) | E → zapis gry, odpoczynek (pełne HP). Obok jest punkt odrodzenia |
| `Weapons` (Ammu-Nation) | bronie, amunicja, apteczka, **kamizelka** (pancerz przyjmuje 80% obrażeń) |
| `Store` (24/7) | jedzenie; **napad**: wyceluj bronią w kasjera przez 3 s → $300–900 i 2 gwiazdki (kasa pusta przez 3 min) |
| `Bar` | jedzenie i picie (leczy) |
| `Repair` (Pay'n'Spray) | wjedź autem → naprawa, gubi gwiazdki, jeśli policja cię nie widzi |

Nowe wnętrze: dopisz wiersz do tablicy `Interiors`, np. `(2, 4, 0, 1, ShopType.Bar, "Mój bar")`. To znaczy: kwartał (2,4), działka (0,1). Potem przebuduj miasto.
Ceny i jedzenie zmieniasz w `ShopZone` (Inspector) i `ShopClient.StoreFood/BarFood`.

**Ruch uliczny i boty** (`RoadNetwork`, `Vehicle`):
- Auta AI jeżdżą po **wszystkich ulicach** i obwodnicy, prawym pasem, losując skręty na skrzyżowaniach (`RoadNetwork` to węzły + odcinki).
- Czujnik przed autem (BoxCast) hamuje przed autami, pieszymi, graczami i ścianami.
- Auta czekają, aż skrzyżowanie będzie wolne, i wyprzedzają auto, które stoi przed nimi dłużej niż 5 s.
- Piesi omijają się nawzajem i auta (`NavMeshObstacle`), uskakują przed rozpędzonym autem i wolą chodniki: jezdnia to obszar NavMesh nr 3 o wyższym koszcie (`PedestrianAI.roadCost`).
- Test automatyczny liczy zderzenia botów (`Vehicle.AiCrashes`, `AiPedHits`). W 80-sekundowym teście było 0 i 0.

**Pozostałe dodatki**:
- **Dzień i noc** (`DayNightCycle`, doba = 12 min, zsynchronizowana czasem serwera). W nocy świecą latarnie i reflektory aut.
- **Zapis gry** po nicku (kasa, bronie, pancerz) w `%USERPROFILE%\AppData\LocalLow\DefaultCompany\MiniGTA\saves`. Zapisuje host. Wyłączysz go w prefabie Player → PlayerStats → `enableSaving`.
- **Regeneracja zdrowia** do 50 HP po 8 s bez obrażeń (PlayerController).
- **Skoki kaskaderskie**: ponad 1,2 s w powietrzu = bonus $ (stunt park, skocznie przy obwodnicy).
- **Misja „Wyścig po obwodnicy”**: punkty kontrolne na czas. Własne auto dla kroku misji ustawisz w polu `vehicleOverride`.
- **Mapa (M)** z ikonami sklepów, misji i graczy.

### 5.11 Sygnalizacja świetlna i chodniki
Kod generujący: `Editor/GTASceneBuilder.Streets.cs`.

**Sygnalizacja** (`Scripts/World/TrafficSignal.cs`) stoi na każdym skrzyżowaniu z co najmniej 3 wlotami.
- Ma dwie fazy: ruch wzdłuż X i wzdłuż Z. Cykl: zielone 11 s → żółte 3 s → wszystkie czerwone 2 s.
- Czasy zmienisz w komponencie (`greenTime`, `yellowTime`, `allRedTime`).
- `offset` przesuwa fazę, dzięki czemu sąsiednie skrzyżowania tworzą „zieloną falę”.
- Faza jest liczona z czasu serwera, więc wszyscy gracze widzą to samo bez wysyłania danych przez sieć.

**Auta AI** (`Vehicle.UpdateRoam`):
- Na czerwonym stają przed pasami (`RoadNetwork.signalStopDistance`).
- Na żółtym hamują tylko wtedy, gdy zdążą.
- W kolejce do świateł nie wyprzedzają.

Gracz może przejechać na czerwonym. Jeśli chcesz za to gwiazdkę, dopisz w `Vehicle` sprawdzenie `TrafficSignal.At(...)` i wywołanie `WantedSystem.ReportCrime`.

**Przejścia dla pieszych**: zebry na każdym wlocie, linie zatrzymania, osobny obszar NavMesh „Crosswalk” (nr 4).
- Piesi przechodzą przez jezdnię prawie wyłącznie po pasach (`PedestrianAI.roadCost` = 40).
- Czekają, aż auta na przecinanej ulicy dostaną czerwone (`obeyTrafficLights`).
- Uciekający piesi biegną mimo czerwonego.

**Chodniki**: płyty z teksturą, krawężniki, kosze, ławki i hydranty.
- Wnętrze kwartału to obszar NavMesh „Lot” (nr 5). Piesi mogą tam wejść, np. do sklepu, ale spacerują chodnikiem.
- Szerokość chodnika ustawisz zmienną `ring` w `BuildBlocks`.
- Tekstury płyt i zebry są generowane w kodzie, w `Generated/Textures`. Możesz je podmienić na własne.

### 5.12 Służby, wojsko i zapis gry
**Poziomy reakcji** (`Scripts/Spawning/PoliceSpawner.cs`, tabela `forces` w Inspectorze, indeks = gwiazdki):

| Gwiazdki | Kto przyjeżdża |
|---|---|
| 1–2 | policja pieszo i radiowozy |
| 3–4 | SWAT: mocniejsi, z większym pancerzem; dojeżdżają vanami SWAT |
| 5 | wojsko: żołnierze i wojskowe transportery (plus SWAT) |

- Prefaby: `swatPrefab`, `armyPrefab`, `armyCarPrefab`. Gdy ich nie ma, używany jest prefab niższego poziomu. Własne modele podłączysz w tych polach.
- Statystyki (zdrowie, obrażenia, celność) ustawiasz w prefabach jednostek.

**Przejazd na czerwonym** (`Scripts/World/TrafficViolations.cs`): auto gracza, które przecina linię stopu przy czerwonym, daje 1★. Liczy się raz za każdym przejazdem. Ilość gwiazdek zmienisz w `starsForRedLight`.

**Zapis gry**:
- Zapis wczytuje się po nicku. Domyślny nick to `Gracz`, bez losowej liczby, więc po restarcie gra wczyta ten sam zapis.
- Zapis zapisuje się w kryjówce, co 30 s i przy zamknięciu gry (także wyjście z okna).
- W menu pod polem nicka widać datę ostatniego zapisu dla tego nicka.
- Zapisy są w `%USERPROFILE%\AppData\LocalLow\DefaultCompany\MiniGTA\saves`. Nazwa pliku to nick małymi literami.

### 5.13 Czołgi, helikoptery i konsola
**Czołg** (wojsko, 5★, `Scripts/Vehicles/Tank.cs`): 2000 HP, wolny (~25 km/h). Wsiadasz jak do auta, LPM strzela z działa tam, gdzie celuje kamera. Pocisk wybucha w pierwszym trafieniu. Strzela tylko serwer. Czołg AI goni gracza i strzela w zasięgu 70 m. Ustawienia: `reload`, `shellDamage`, `range`.

**Helikopter** (policja, 3★ i wyżej, `Scripts/Vehicles/Helicopter.cs`):
- Policyjny krąży nad graczem i strzela. Zestrzelony spada.
- Po dotknięciu ziemi lub dachu wraca do życia i staje się zwykłym pojazdem, którym można latać.
- Sterowanie pilota: W/S przód i tył, A/D obrót, Spacja w górę, Shift w dół.
- Helikopter bez pilota powoli ląduje.
- Ilość w tabeli `forces` (`helis`, `tanks`) w `PoliceSpawner`.

**Konsola** (klawisz `` ` ``, Esc zamyka). Komendy wykonuje serwer. Działają też polskie nazwy bez ogonków:

| Komenda | Co robi |
|---|---|
| `gwiazdki 0-5` | ustawia poziom poszukiwania |
| `nieśmiertelnosc` | przełącza nieśmiertelność |
| `pieniadze N` | dodaje pieniądze |
| `bronie` | wszystkie bronie |
| `zdrowie` | pełne zdrowie i kamizelka |
| `pojazd czolg` / `heli` / `radiowoz` / `wojsko` / `sport` / `taxi` / `sedan` | spawnuje pojazd przed graczem |
| `czysc` / `pomoc` | czyści okno / pokazuje listę |

Konsola działa tylko, gdy `allowCheats` jest włączone w prefabie gracza (`DebugCheats`). Przed wydaniem gry wyłącz to.

## 6. Ważne zasady (żeby multiplayer działał)

1. **Każdy prefab, który się spawnuje w trakcie gry, musi być na liście `MiniGTANetworkPrefabs`** i mieć `NetworkObject`. Menu 1 i 5 dopisują automatycznie wszystko, co jest podpięte do spawnerów i misji. Ręcznie dodane prefaby sprawdź sam: inaczej host je widzi, a klienci nie.
2. Zmiany stanu gry (HP, kasa, gwiazdki, misje) rób **na serwerze** (`if (!IsServer) return;`). Klient prosi serwer przez `[Rpc(SendTo.Server)]`.
3. Logikę AI uruchamiaj tylko na serwerze, klienci tylko wyświetlają.
4. Sterowanie (input) tylko u właściciela: `if (!IsOwner) return;`.
5. Przed wydaniem gry wyłącz kody: prefab Player → DebugCheats → `allowCheats = false`.

## 7. Ograniczenia prototypu

- Grafika z prymitywów, brak dźwięków i animacji (patrz 5.5 i 5.8).
- Amunicja liczona u klienta: dla pełnego anty-cheata przenieś ją na serwer.
- Gracz nie może przejechać innego gracza (hitbox gracza jest triggerem); NPC – tak.
- Policja nie ma helikopterów ani blokad drogowych: miejsce na rozbudowę jest w `PoliceSpawner`.

## 8. Test automatyczny

`MiniGTA.exe -batchmode -nographics -host -autotest` (oraz drugi proces z `-client -ip 127.0.0.1 -autotest`)
uruchamia test dymny: spawn auta, wsiadanie, jazda, 2 gwiazdki, policja. W logu szukaj linii z `[AUTOTEST]` i `[DMG]`.

---

## 9. Tryb fabularny „Poza zasięgiem” (singleplayer)

- Menu: **Kontynuuj / Nowa gra / Postać**. Gra startuje jako lokalny host (127.0.0.1) – multiplayer nie jest potrzebny.
- **Kampania**: 11 misji w 3 aktach (czerwone znaczniki), generowane w `Editor/GTASceneBuilder.Story.cs`.
  Misje odblokowują się łańcuchem flag (`requiresFlag` → `setsFlag`), dialogi w `intro` / `outro` (Enter = dalej). Flagi: `Story.cs`.
- **Przebieralnia (F2)**: kolory skóry, włosów, góry, dołu, butów + numer stroju. `CharacterAppearance.cs`.
- **Każdy NPC dostaje losowy strój i kolory** (policja w mundurach). Kolory idą przez MaterialPropertyBlock, więc paczka zostaje nietknięta.
- **Postacie z paczki**: zaimportuj „Creative Characters”, potem **MiniGTA → 6. Podłącz postacie z paczki**.
  Narzędzie dodaje modele jako stroje `Outfit_Body_N` do gracza i wszystkich NPC oraz tworzy Animator (Speed / Dead / Shoot).
- Kolejność po imporcie paczek: menu **1** (miasto + misje) → **5** (bronie, auta) → **6** (postacie).
