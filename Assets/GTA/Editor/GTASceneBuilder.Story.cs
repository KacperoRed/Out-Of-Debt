using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Kampania „Out of Debt” – 11 misji w 3 aktach. Każda misja wymaga flagi z poprzedniej (requiresFlag → setsFlag),
    /// więc fabuła idzie po kolei, a znaczniki pojawiają się dopiero, gdy misja jest dostępna.
    /// Teksty dialogów i punkty zmienisz tutaj albo w Inspectorze obiektów Mission_* w scenie.
    /// </summary>
    public static partial class GTASceneBuilder
    {
        static DialogueLine L(string who, string text) => new DialogueLine(who, text);

        static Mission StoryMission(Transform root, string obj, string name, string chapter, string req, string sets,
            Vector3 pos, int reward, string desc, DialogueLine[] intro, DialogueLine[] outro)
        {
            var m = Empty(obj, root, pos).gameObject.AddComponent<Mission>();
            m.missionName = name;
            m.chapter = chapter;
            m.requiresFlag = req;
            m.setsFlag = sets;
            m.description = desc;
            m.moneyReward = reward;
            m.repeatable = false;
            m.isStory = true;
            m.debtPayment = DebtFor(obj);
            m.intro = intro;
            m.outro = outro;
            m.markerColor = new Color(1f, 0.3f, 0.15f, 0.65f); // misje fabularne czerwono-pomarańczowe, poboczne żółte
            return m;
        }

        /// <summary>Punkt na chodniku tuż przed drzwiami budynku z wnętrzem (kwartał, działka) – tu startują misje.</summary>
        static Vector3 Door(int bx, int bz, int lx, int lz)
        {
            var lot = LotCenter(bx, bz, lx, lz);
            return new Vector3(lot.x + (lx == 0 ? -13.5f : 13.5f), 0.2f, lot.z);
        }

        static int DebtFor(string obj)
        {
            switch (obj)
            {
                case "Story_1_Dlug": return 2000;
                case "Story_2_Rata": return 2500;
                case "Story_3_Pogon": return 3000;
                case "Story_4_Kontakty": return 1500;
                case "Story_5_Towar": return 3000;
                case "Story_6_Konkurencja": return 3000;
                case "Story_7_Zdrajca": return 3000;
                default: return 0;
            }
        }

        static Vector3 At(int ix, int iz, float off = 3f) => new Vector3(RoadCenter(ix) + off, 0.2f, RoadCenter(iz) + off);

        static MissionStep Talk(string who, string objective, Transform at, params DialogueLine[] lines) =>
            new MissionStep { type = MissionStepType.Talk, objective = objective, target = at, radius = 7f, contact = who, lines = lines };

        static MissionStep Cine(params DialogueLine[] lines) =>
            new MissionStep { type = MissionStepType.Dialogue, objective = "…", lines = lines };

        static MissionStep Route(string objective, float radius, float timeLimit, params Transform[] pts) =>
            new MissionStep { type = MissionStepType.Checkpoints, objective = objective, points = pts, radius = radius, timeLimit = timeLimit };

        static void BuildStoryMissions(Transform root, Prefabs pf)
        {
            float H = HighwayRing;
            const string Rico = "Rico", Hero = "Ty", Mara = "Mara", Vik = "Viktor", Tony = "Tony", Pablo = "Pablo";
            var casino = LotCenter(3, 5, 0, 0); // Kasyno Royal – drzwi od strony zachodniej ulicy
            var casinoDoor = new Vector3(casino.x - 13.5f, 0.2f, casino.z);

            // ================= AKT I – DNO =================
            {
                var m = StoryMission(root, "Story_1_Dlug", "I. Dług", "Akt I – Dno", null, "a1_1", Door(3, 6, 1, 0), 1500,
                    "Rico chce pierwszej raty. Odbierz auto, zrób objazd po mieście i dowieź je człowiekowi Rica.",
                    new[] { L(Rico, "Dwadzieścia tysięcy, kolego. Tyle jesteś mi winien i ani centa nie widzę. Raty będziesz spłacał robotą."),
                            L(Hero, "Dasz mi trochę czasu."),
                            L(Rico, "Czas masz do końca tygodnia. Na początek odbierz auto z placu i dowieź je do mojego człowieka.") },
                    new[] { L(Rico, "Nie było źle. Ale to dopiero początek.") });
                m.contact = Rico;
                var car = MissionPoint(m.transform, "CarSpot", At(6, 1), 0);
                var p1 = MissionPoint(m.transform, "P1", At(5, 3)); var p2 = MissionPoint(m.transform, "P2", At(4, 5));
                var drop = MissionPoint(m.transform, "Drop", At(2, 7));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.EnterVehicle, objective = "Wsiądź do auta Rica", target = car, spawnVehicleAtTarget = true },
                    Route("Zabierz paczki z dwóch punktów", 9f, 120f, p1, p2),
                    new MissionStep { type = MissionStepType.DriveTo, objective = "Dowieź auto do człowieka Rica", target = drop, radius = 12f, timeLimit = 150f },
                    Talk(Tony, "Oddaj paczki Tony'emu", drop, L(Tony, "Rico mówił, że przyjedziesz. Paczki są całe? Dobra, powiem mu, że można na ciebie liczyć."), L(Hero, "To jak, dług się zmniejszył?"), L(Tony, "Trochę. Nie ciesz się za wcześnie.")),
                };
            }
            {
                var m = StoryMission(root, "Story_2_Rata", "I. Pierwsza rata", "Akt I – Dno", "a1_1", "a1_2", Door(3, 6, 1, 0), 2500,
                    "Konkurencja Rica pobiera haracz na twojej ulicy. Odeprzyj dwie fale poborców.",
                    new[] { L(Rico, "Chłopaki z Zachodu zbierają haracz na moim terenie. Wyjaśnij im to.") },
                    new[] { L(Rico, "Lubię ludzi, którzy rozumieją aluzje.") });
                m.contact = Rico;
                var turf = MissionPoint(m.transform, "Turf", At(8, 2));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.GoTo, objective = "Jedź do dzielnicy przemysłowej", target = turf, radius = 25f },
                    Cine(L(Rico, "Są tam. Pierwsza grupa to zwiadowcy, za chwilę przyjdą następni. Nie daj się otoczyć.")),
                    new MissionStep { type = MissionStepType.KillEnemies, objective = "Rozprosz poborców", target = turf, enemyCount = 3, waves = 2, radius = 12f },
                };
            }
            {
                var m = StoryMission(root, "Story_3_Pogon", "I. Nocny kurier", "Akt I – Dno", "a1_2", "a1_3", Door(2, 6, 0, 0), 3500,
                    "Dostawa poszła źle – policja jest za tobą. Przeżyj, przebij się przez trasę ucieczki i zgub pościg.",
                    new[] { L(Mara, "Słuchaj uważnie, mamy kłopot. Ktoś doniósł na twoją ostatnią robotę."),
                            L(Mara, "Policja już jedzie. Masz pół minuty.") },
                    new[] { L(Mara, "Dobrze się spisałeś. Przyda ci się ktoś taki jak ja. Spotkajmy się w centrum.") });
                m.contact = Mara;
                var r1 = MissionPoint(m.transform, "R1", At(4, 6)); var r2 = MissionPoint(m.transform, "R2", At(5, 4)); var r3 = MissionPoint(m.transform, "R3", At(3, 2));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.Survive, objective = "Uciekaj przed policją", duration = 30f, setWantedOnStart = 2 },
                    Route("Przebij się trasą ucieczki", 10f, 120f, r1, r2, r3),
                    new MissionStep { type = MissionStepType.LoseWanted, objective = "Zgub pościg" },
                };
            }

            // ================= AKT II – WSPINACZKA =================
            {
                var m = StoryMission(root, "Story_4_Kontakty", "II. Człowiek z kontaktami", "Akt II – Wspinaczka", "a1_3", "a2_1", Door(2, 6, 0, 0), 3000,
                    "Mara przedstawi cię Viktorowi, który zna miasto od środka. Zdaj jego egzamin.",
                    new[] { L(Mara, "Viktor jest w barze w śródmieściu. Zna kasyno jak własną kieszeń."),
                            L(Hero, "Jakie kasyno?"),
                            L(Mara, "Takie, w którym ktoś zawsze wygrywa. I nigdy nie jesteś to ty.") },
                    new[] { L(Vik, "Mara dużo o tobie mówiła. Jeśli chcesz coś w tym mieście znaczyć, najpierw musisz zarobić.") });
                m.contact = Mara;
                var bar = MissionPoint(m.transform, "Bar", Door(7, 3, 1, 1));
                var t1 = MissionPoint(m.transform, "T1", At(7, 4)); var t2 = MissionPoint(m.transform, "T2", At(6, 3)); var t3 = MissionPoint(m.transform, "T3", At(8, 3));
                m.steps = new[]
                {
                    Talk(Vik, "Spotkaj Viktora w barze", bar, L(Vik, "Więc to ty. Zanim ci zaufam, zrobisz mi mały objazd po moich punktach."), L(Vik, "Trzy miejsca. Zajrzyj, sprawdź, czy nikt się nie kręci.")),
                    Route("Sprawdź trzy punkty Viktora", 9f, 150f, t1, t2, t3),
                    Cine(L(Vik, "Pusto? Dobrze. Wiem już, że mam z kim pracować."), L(Hero, "To znaczy, że jest robota?"), L(Vik, "Jest. Wróć do mnie, gdy będziesz gotowy.")),
                };
            }
            {
                var m = StoryMission(root, "Story_5_Towar", "II. Odbiór towaru", "Akt II – Wspinaczka", "a2_1", "a2_2", Door(7, 3, 1, 1), 5000,
                    "Zabierz sportowe auto ze skradzionym towarem i dowieź je odbiorcy, zanim policja go odnajdzie.",
                    new[] { L(Vik, "Mam towar do przewiezienia. Sportowe auto, szybko i bez pytań."),
                            L(Vik, "Policja węszy. Lepiej, żeby cię nie złapali.") },
                    new[] { L(Vik, "Szybki jesteś. Takiego kierowcy potrzebuję.") });
                m.contact = Vik;
                var car = MissionPoint(m.transform, "Car", At(7, 2), 0);
                var v1 = MissionPoint(m.transform, "V1", At(5, 2)); var v2 = MissionPoint(m.transform, "V2", At(3, 4));
                var drop = MissionPoint(m.transform, "Drop", At(1, 7));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.EnterVehicle, objective = "Wsiądź do sportowego auta", target = car, spawnVehicleAtTarget = true, vehicleOverride = pf.cars[3] },
                    Route("Trasa do odbiorcy – dwa punkty", 12f, 100f, v1, v2),
                    new MissionStep { type = MissionStepType.DriveTo, objective = "Dowieź towar", target = drop, radius = 12f, timeLimit = 100f, setWantedOnStart = 2 },
                    new MissionStep { type = MissionStepType.LoseWanted, objective = "Zgub policję" },
                    Talk(Pablo, "Przekaż towar Pablo", drop, L(Pablo, "Spóźniony o minutę, ale cały. Viktor wiedział, kogo wysłać."), L(Pablo, "Powiedz mu, że jesteśmy kwita.")),
                };
            }
            {
                var m = StoryMission(root, "Story_6_Konkurencja", "II. Konkurencja", "Akt II – Wspinaczka", "a2_2", "a2_3", Door(7, 3, 1, 1), 6000,
                    "Gang z Zachodu zagraża interesom Viktora. Rozbij trzy fale ich ludzi w magazynie.",
                    new[] { L(Vik, "Mają magazyn w strefie przemysłowej. Chcą przejąć kasyno.") },
                    new[] { L(Vik, "Czysta robota. Zostało jeszcze jedno: ktoś nas sprzedaje.") });
                m.contact = Vik;
                var ware = MissionPoint(m.transform, "Warehouse", At(8, 1));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.GoTo, objective = "Jedź do magazynu", target = ware, radius = 25f },
                    Cine(L(Vik, "Wchodzą wszyscy naraz, więc rozbijaj ich falami. Pilnuj tyłów.")),
                    new MissionStep { type = MissionStepType.KillEnemies, objective = "Rozbij gang", target = ware, enemyCount = 4, waves = 3, radius = 14f },
                };
            }
            {
                var m = StoryMission(root, "Story_7_Zdrajca", "II. Zdrajca", "Akt II – Wspinaczka", "a2_3", "a2_4", Door(5, 7, 1, 1), 8000,
                    "Zdrajca doniósł policji. Przetrwaj obławę, uciekaj trasą wskazaną przez Marę i zgub pościg.",
                    new[] { L(Mara, "To była pułapka! Policja jest wszędzie, wiej!") },
                    new[] { L(Mara, "Przeżyliśmy. Teraz mamy jedną szansę, żeby wyrwać się z tego miasta: kasyno.") });
                m.contact = Mara;
                var z1 = MissionPoint(m.transform, "Z1", At(6, 6)); var z2 = MissionPoint(m.transform, "Z2", At(4, 3)); var z3 = MissionPoint(m.transform, "Z3", At(2, 2));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.Survive, objective = "Przetrwaj obławę", duration = 40f, setWantedOnStart = 3 },
                    Route("Uciekaj trasą Mary", 10f, 120f, z1, z2, z3),
                    new MissionStep { type = MissionStepType.LoseWanted, objective = "Zgub pościg" },
                };
            }

            // ================= AKT III – KASYNO =================
            {
                var m = StoryMission(root, "Story_8_Plan", "III. Plan", "Akt III – Kasyno", "a2_4", "a3_1", Door(7, 3, 1, 1), 4000,
                    "Obejrzyj kasyno z zewnątrz, obejdź je dookoła i omów plan z ekipą.",
                    new[] { L(Vik, "W skarbcu leży dość gotówki, żeby kupić nowe życie."),
                            L(Mara, "Skarbiec jest poza zasięgiem, chyba że zdobędziemy karty dostępu ochrony."),
                            L(Hero, "To znaczy, że zaczynamy od ochrony.") },
                    new[] { L(Vik, "Najpierw karty. Ludzie z karnetami czekają na zachodzie.") });
                m.contact = Vik;
                var look = MissionPoint(m.transform, "Casino", casinoDoor);
                var c1 = MissionPoint(m.transform, "C1", new Vector3(casino.x - 16f, 0.2f, casino.z - 22f));
                var c2 = MissionPoint(m.transform, "C2", new Vector3(casino.x + 16f, 0.2f, casino.z - 22f));
                var c3 = MissionPoint(m.transform, "C3", new Vector3(casino.x + 16f, 0.2f, casino.z + 22f));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.GoTo, objective = "Obejrzyj kasyno", target = look, radius = 20f },
                    Route("Obejdź kasyno dookoła (kamery i wejścia)", 9f, 0f, c1, c2, c3),
                    Cine(L(Mara, "Dwa wejścia, jedno służbowe. Karty mają ochroniarze z zachodniej dzielnicy."), L(Vik, "Wiesz, co robić.")),
                };
            }
            {
                var m = StoryMission(root, "Story_9_Karty", "III. Karty dostępu", "Akt III – Kasyno", "a3_1", "a3_2", Door(2, 3, 0, 0), 7000,
                    "Odbierz karty dostępu ochroniarzom kasyna – pokonaj trzy ekipy po kolei.",
                    new[] { L(Mara, "Mają karty, ale nie oddadzą ich po dobroci.") },
                    new[] { L(Mara, "Mamy karty. Jutro w nocy kasyno będzie nasze.") });
                m.contact = Mara;
                var guards = MissionPoint(m.transform, "Guards", At(2, 8));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.GoTo, objective = "Znajdź ochroniarzy", target = guards, radius = 25f },
                    new MissionStep { type = MissionStepType.KillEnemies, objective = "Odbierz karty", target = guards, enemyCount = 3, waves = 3, radius = 12f },
                    Cine(L(Mara, "Masz je? Super. Wracaj, zanim ktoś zauważy, że ich brakuje.")),
                };
            }
            {
                var m = StoryMission(root, "Story_10_Napad", "III. Napad na kasyno", "Akt III – Kasyno", "a3_2", "a3_3", casinoDoor, 15000,
                    "Wejdź do kasyna, rozbij dwie fale ochrony i dotrwaj, aż ekipa opróżni skarbiec.",
                    new[] { L(Vik, "Jedziemy. Kiedy włączy się alarm, masz trzydzieści sekund, żeby utrzymać ochronę z dala od skarbca."),
                            L(Mara, "Powodzenia. Jesteśmy poza zasięgiem prawa. Na razie.") },
                    new[] { L(Vik, "Mamy gotówkę! Wiej, zanim ściągną wojsko!") });
                m.contact = Vik;
                var door = MissionPoint(m.transform, "Entrance", casino);
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.GoTo, objective = "Wejdź do kasyna", target = door, radius = 6f },
                    new MissionStep { type = MissionStepType.KillEnemies, objective = "Rozbij ochronę", target = door, enemyCount = 4, waves = 2, radius = 14f, setWantedOnStart = 2 },
                    Cine(L(Mara, "Alarm! Skarbiec się otwiera, trzymaj ich z dala!")),
                    new MissionStep { type = MissionStepType.Survive, objective = "Utrzymaj pozycję do końca napadu", duration = 30f, setWantedOnStart = 4 },
                };
            }
            {
                var m = StoryMission(root, "Story_11_Ucieczka", "III. Poza zasięgiem", "Akt III – Kasyno", "a3_3", "a3_4", casinoDoor, 30000,
                    "Uciekaj z łupem przez miasto i obwodnicę. Zgub pościg i uwolnij się od długu.",
                    new[] { L(Mara, "Samochód czeka. Wojsko zablokuje miasto za chwilę, jedź!") },
                    new[] { L(Rico, "Cóż, dług spłacony. Zostaje pytanie, gdzie się podziejesz."),
                            L(Hero, "Tam, gdzie nikt nie sięgnie."),
                            L("Narrator", "KONIEC. Dziękujemy za grę! Miasto jest wciąż twoje – dokończ zlecenia poboczne albo szukaj kolejnych wyzwań.") });
                m.contact = Mara;
                var car = MissionPoint(m.transform, "GetawayCar", new Vector3(RoadCenter(3) + 3f, 0.3f, casino.z), 0);
                var f1 = MissionPoint(m.transform, "F1", At(3, 3)); var f2 = MissionPoint(m.transform, "F2", At(1, 4));
                var exit = MissionPoint(m.transform, "Exit", new Vector3(-H, 0.2f, casino.z));   // zachodnia obwodnica na wysokości kasyna
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.EnterVehicle, objective = "Wsiądź do auta ucieczkowego", target = car, spawnVehicleAtTarget = true, vehicleOverride = pf.cars[3], setWantedOnStart = 4 },
                    Route("Przebij się przez miasto", 14f, 120f, f1, f2),
                    new MissionStep { type = MissionStepType.DriveTo, objective = "Dojedź na obwodnicę", target = exit, radius = 25f, timeLimit = 180f },
                    new MissionStep { type = MissionStepType.LoseWanted, objective = "Zgub pościg i wyjdź poza zasięg" },
                };
            }
        }
    }
}
