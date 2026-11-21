using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>Nick i pieniądze gracza (zsynchronizowane). Pieniądze zmienia tylko serwer.</summary>
    public class PlayerStats : NetworkBehaviour
    {
        public int startMoney = 500;
        [Tooltip("Zapis postępu na serwerze (po nicku) – patrz SaveSystem")]
        public bool enableSaving = true;
        public float autosaveInterval = 30f;

        bool _nameSet;
        float _nextAutosave;

        public readonly NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>("Gracz");
        public readonly NetworkVariable<int> Money = new NetworkVariable<int>(0);

        // ---------- plecak: liczby sztuk 5 przedmiotów (po 8 bitów) w jednej zmiennej; zmienia tylko serwer ----------
        public const int PackItems = 5, PackMax = 9;
        public readonly NetworkVariable<ulong> Pack = new NetworkVariable<ulong>(0);

        public int PackCount(int item) => item < 0 || item >= PackItems ? 0 : (int)((Pack.Value >> (item * 8)) & 0xFFUL);

        void SetPackCount(int item, int n) =>
            Pack.Value = (Pack.Value & ~(0xFFUL << (item * 8))) | ((ulong)Mathf.Clamp(n, 0, PackMax) << (item * 8));

        /// <summary>Serwer: dodaj przedmiot do plecaka. false = plecak pełny dla tego przedmiotu.</summary>
        public bool AddPackItem(int item, int n = 1)
        {
            if (!IsServer || item < 0 || item >= PackItems || PackCount(item) + n > PackMax) return false;
            SetPackCount(item, PackCount(item) + n);
            return true;
        }

        /// <summary>Przelej część gotówki na spłatę długu (host liczy dług w Story).</summary>
        public void RequestPayDebt(int amount) { if (IsOwner) PayDebtRpc(amount); }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void PayDebtRpc(int amount)
        {
            if (Story.Debt <= 0) { Note("Nie masz już długu", false); return; }
            amount = Mathf.Min(amount, Story.Payable);
            if (amount <= 0) { Note("Resztę długu spłacisz dopiero po wykonaniu wszystkich zadań (misji)", false); return; }
            if (!TrySpend(amount)) { Note("Za mało gotówki", false); return; }
            Story.PayDebt(amount);
            if (Story.Debt <= 0) { Story.Set("debt_paid"); TriggerEnding(); }
            Note($"Spłacono ${amount:N0} długu (zostało ${Story.Debt:N0})", true);
        }

        /// <summary>Serwer: dług spłacony – raz na grę pokaż zakończenie (katscena, WYGRAŁEŚ, wybór).</summary>
        public void TriggerEnding()
        {
            if (!IsServer || Story.HasFlagStrict("ending_done")) return;
            Story.Set("ending_done");
            EndingRpc();
        }

        [Rpc(SendTo.Owner)]
        void EndingRpc() => EndingUI.Begin();

        public void RequestUsePack(int item) { if (IsOwner) UsePackRpc(item); }

        /// <summary>Skrót Z: apteczka przy małym zdrowiu, w przeciwnym razie jedzenie / napój / apteczka.</summary>
        public void QuickHeal()
        {
            if (!IsOwner) return;
            var hp = GetComponent<Health>();
            if (hp == null) return;
            int[] order = hp.Current.Value <= hp.maxHealth * 0.5f
                ? new[] { BackpackItems.Medkit, BackpackItems.Food, BackpackItems.Drink }
                : new[] { BackpackItems.Food, BackpackItems.Drink, BackpackItems.Medkit };
            foreach (int it in order) if (PackCount(it) > 0) { UsePackRpc(it); return; }
            HUD.Notify("Plecak: brak przedmiotów do leczenia", new Color(1f, 0.4f, 0.4f));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void UsePackRpc(int item)
        {
            var hp = GetComponent<Health>();
            if (item < 0 || item >= PackItems || hp == null || hp.Dead) return;
            if (PackCount(item) <= 0) { Note("Nie masz tego w plecaku", false); return; }
            switch (item)
            {
                case BackpackItems.Medkit:
                    if (hp.Current.Value >= hp.maxHealth) { Note("Masz pełne zdrowie", false); return; }
                    hp.Heal(1000f); break;
                case BackpackItems.Vest:
                    if (hp.Armor.Value >= Health.MaxArmor) { Note("Masz pełny pancerz", false); return; }
                    hp.AddArmor(Health.MaxArmor); break;
                case BackpackItems.Food:
                case BackpackItems.Drink:
                    if (hp.Current.Value >= hp.maxHealth) { Note("Nie jesteś głodny", false); return; }
                    hp.Heal(item == BackpackItems.Food ? 35f : 15f); break;
                case BackpackItems.AmmoPack:
                    var wc = GetComponent<WeaponController>();
                    if (wc != null) wc.AddAmmoServer(1);
                    break;
            }
            SetPackCount(item, PackCount(item) - 1);
            Note("Użyto: " + BackpackItems.Names[item], true);
        }

        void Note(string text, bool ok)
        {
            var fs = new FixedString64Bytes();
            fs.CopyFromTruncated(text);
            NoteRpc(fs, ok);
        }

        [Rpc(SendTo.Owner)]
        void NoteRpc(FixedString64Bytes text, bool ok) => HUD.Notify(text.ToString(), ok ? Color.green : new Color(1f, 0.4f, 0.4f));

        public override void OnNetworkSpawn()
        {
            if (IsServer) { Money.Value = startMoney; AddPackItem(BackpackItems.Medkit, 1); }
            if (IsOwner) SetNameRpc(NetworkUI.LocalPlayerName);
            Money.OnValueChanged += OnMoneyChanged;
        }

        public override void OnNetworkDespawn()
        {
            Money.OnValueChanged -= OnMoneyChanged;
            if (IsServer && _nameSet && enableSaving) SaveSystem.Save(GetComponent<PlayerController>());
        }

        /// <summary>Zamknięcie gry (host): zapis, zanim proces zniknie – także gdy rozłączenie nie zdążyło się wykonać.</summary>
        void OnApplicationQuit()
        {
            if (IsServer && _nameSet && enableSaving) SaveSystem.Save(GetComponent<PlayerController>());
        }

        void Update()
        {
            if (!IsServer || !_nameSet || !enableSaving || Time.time < _nextAutosave) return;
            _nextAutosave = Time.time + autosaveInterval;
            SaveSystem.Save(GetComponent<PlayerController>());
        }

        /// <summary>Serwer: zapisz teraz (kryjówka).</summary>
        public void SaveNow()
        {
            if (IsServer && enableSaving) SaveSystem.Save(GetComponent<PlayerController>());
        }

        void OnMoneyChanged(int oldV, int newV)
        {
            if (IsOwner && oldV != 0 && newV != oldV)
                HUD.Notify(newV > oldV ? $"+${newV - oldV}" : $"-${oldV - newV}", newV > oldV ? Color.green : Color.red);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SetNameRpc(FixedString32Bytes newName)
        {
            string s = newName.ToString().Trim();
            if (string.IsNullOrEmpty(s)) s = "Gracz " + OwnerClientId;
            PlayerName.Value = s;
            if (_nameSet) return;
            _nameSet = true;
            _nextAutosave = Time.time + autosaveInterval;
            if (enableSaving && SaveSystem.Load(GetComponent<PlayerController>(), s)) LoadedRpc();
        }

        [Rpc(SendTo.Owner)]
        void LoadedRpc() => HUD.Notify("Wczytano zapis gry", new Color(0.6f, 0.9f, 1f));

        /// <summary>Tylko serwer. Ujemna kwota = kara. Saldo nie spada poniżej 0.</summary>
        public void AddMoney(int amount)
        {
            if (!IsServer) return;
            Money.Value = Mathf.Max(0, Money.Value + amount);
        }

        public bool TrySpend(int amount)
        {
            if (!IsServer || Money.Value < amount) return false;
            Money.Value -= amount;
            return true;
        }
    }
}
