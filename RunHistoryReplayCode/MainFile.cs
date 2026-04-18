using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Exceptions;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace RunHistoryReplay.RunHistoryReplayCode;

//You're recommended but not required to keep all your code in this package and all your assets in the RunHistoryReplay folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "RunHistoryReplay"; //At the moment, this is used only for the Logger and harmony names.

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        Harmony harmony = new(ModId);

        harmony.PatchAll();
    }

    [HarmonyPatch]
    public static class RunHistoryReplayPatch
    {
        private static bool _useDefaultNodeStuff = true;
        private static RunHistory? _currentRun;
        private static Hashtable? _charactersById = null;
        private static RunHistoryPlayer? _currentPlayer;
        private static Control? _myEventNode;
        

        private static void LogAllChildren(Node parent)
        {
            Logger.Info("All child nodes of: " + parent.GetPath());
            foreach (Node child in parent.GetChildren())
            {
                Logger.Info("  " + child.GetName() + " (" + child.GetType().Name + ")");
            }
        }

        private static void AddOneHundredVajras(SerializablePlayer serializablePlayer)
        {
            string relicCategory = ModelDb.GetCategory(typeof(RelicModel));
            for (int i = 0; i < 100; i++)
            {
                SerializableRelic serializableRelic = new SerializableRelic();
                serializableRelic.Id = ModelDb.GetId<Vajra>();
                serializablePlayer.Relics.Add(serializableRelic);
            }
        }

        private static void AddDramaticEntrance(SerializablePlayer serializablePlayer)
        {
            SerializableCard serializableCard = new SerializableCard();
            serializableCard.Id = ModelDb.GetId<DramaticEntrance>();
            serializablePlayer.Deck.Add(serializableCard);
        }

        private static Player CreatePlayer()
        {
            if (_charactersById == null)
            {
                _charactersById = new Hashtable();
                foreach (CharacterModel character in ModelDb.AllCharacters)
                {
                    _charactersById.Add(character.Id, character);
                }
            }

            if (_currentRun == null)
            {
                Logger.Error("_currentRun is null");
                throw new SingletonInitException();
            }
            List<MapPointHistoryEntry> lastAct = _currentRun.MapPointHistory.Last();
            MapPointHistoryEntry previousRoom = lastAct[lastAct.Count - 2];
            RunHistoryPlayer runHistoryPlayer = _currentPlayer;
            PlayerMapPointHistoryEntry? thePlayerStats = null;
            foreach (PlayerMapPointHistoryEntry playerStats in previousRoom.PlayerStats)
            {
                if (playerStats.PlayerId == runHistoryPlayer.Id)
                {
                    thePlayerStats = playerStats;
                    break;
                }
            }

            if (thePlayerStats == null)
            {
                Logger.Warn("Unable to match PlayerMapPointHistoryEntry with RunHistoryPlayer. Defaulting to first available player.");
                thePlayerStats = previousRoom.PlayerStats.First();
                runHistoryPlayer = _currentRun.Players.First();
            }
            CharacterModel runHistoryCharacter = (CharacterModel) _charactersById[runHistoryPlayer.Character];
            Player player = Player.CreateForNewRun(runHistoryCharacter, UnlockState.all, 1UL);
            SerializablePlayer serializablePlayer = player.ToSerializable();
            serializablePlayer.Relics = (List<SerializableRelic>) runHistoryPlayer.Relics;
            serializablePlayer.Deck = (List<SerializableCard>) runHistoryPlayer.Deck;
            serializablePlayer.CurrentHp = thePlayerStats.CurrentHp;
            serializablePlayer.MaxHp = thePlayerStats.MaxHp;
            AddOneHundredVajras(serializablePlayer);
            AddDramaticEntrance(serializablePlayer);
            player = Player.FromSerializable(serializablePlayer);
            return player;
        }

        private static List<ActModel> GetActModels()
        {
            List<ActModel> actModels = new List<ActModel>();
            for (int i = 0; i < _currentRun.MapPointHistory.Count; i++)
            {
                actModels.Add(ModelDb.GetById<ActModel>(_currentRun.Acts[i]));
            }
            // SerializableActModel serializableActModel = actModels.Last().ToSave();
            // actModels.RemoveAt(actModels.Count - 1);
            // actModels.Add(ActModel.FromSave(serializableActModel));
            return actModels;
        }

        private static SerializableRun createSerializableRun()
        {
            SerializableRun serializableRun = new SerializableRun();
            serializableRun.MapPointHistory = _currentRun.MapPointHistory;
            serializableRun.Ascension = _currentRun.Ascension;
            // serializableRun.Acts = _currentRun.Acts;
            // serializableRun.Players = _currentRun.Players;
            serializableRun.GameMode = _currentRun.GameMode;
            serializableRun.SerializableRng = new SerializableRunRngSet();
            serializableRun.SerializableRng.Seed = _currentRun.Seed;
            return serializableRun;
        }

        private static async Task StartFloorReplay(Player player, RunState runState, NGame game, ModelId roomModelId, int actIndex)
        {
            using (new NetLoadingHandle(RunManager.Instance.NetService))
            {
                Decimal hp = player.Creature.CurrentHp;
                await PreloadManager.LoadRunAssets(runState.Players.Select<Player, CharacterModel>((Func<Player, CharacterModel>) (p => p.Character)));
                await PreloadManager.LoadActAssets(runState.Acts[actIndex]);
                RunManager.Instance.Launch();
                game.RootSceneContainer.SetCurrentScene((Control) NRun.Create(runState));
                await RunManager.Instance.EnterAct(actIndex, false);

                EventModel architectEventModel = ModelDb.Event<TheArchitect>();
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: architectEventModel);
                
                // Reset health after Ancient heals
                player.Creature.SetCurrentHpInternal(hp);
                game.AudioManager.StopMusic();
                if (NMapScreen.Instance != null && NMapScreen.Instance.IsOpen)
                {
                    NMapScreen.Instance.Close();
                }
                EncounterModel encounterModel = ModelDb.GetById<EncounterModel>(roomModelId).ToMutable();
                CombatRoom room = new CombatRoom(new CombatState(encounterModel, runState, runState.Modifiers, runState.MultiplayerScalingModel))
                {
                    ShouldCreateCombat = true,
                    ShouldResumeParentEventAfterCombat = true,
                    ParentEventId = architectEventModel.Id
                };
                _myEventNode = null;
                await RunManager.Instance.EnterRoomWithoutExitingCurrentRoom(room, true);
            }
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(NRunHistory))]
        [HarmonyPatch("_Ready")]
        private static void AfterReady(NRunHistory __instance)
        {
            Logger.Info("Run history opened");
            NGame game = (NGame) __instance.GetTree().CurrentScene;
            LogAllChildren(__instance.GetTree().CurrentScene);
            Button replayFloorButton = new Button();
            replayFloorButton.SetText("Replay Floor");
            replayFloorButton.Pressed += () =>
            {
                _useDefaultNodeStuff = false;
                Player player = CreatePlayer();
                List<ActModel> actModels = GetActModels();
                RunState runState = RunState.CreateForTest([player], actModels, null, GameMode.Standard, _currentRun.Ascension, _currentRun.Seed);
                RunManager.Instance.SetUpTest(runState, new NetSingleplayerGameService());
                RunManager.Instance.GenerateRooms();
                int actIndex = _currentRun.MapPointHistory.Count - 1;
                ModelId roomId = _currentRun.MapPointHistory.Last().Last().Rooms.Last().ModelId;
                StartFloorReplay(player, runState, game, roomId, actIndex);
            };
            __instance.AddChild(replayFloorButton);
            NShareButton shareButton = __instance.GetNode<NShareButton>(new NodePath("ShareButton"));
            replayFloorButton.SetPosition(new Vector2(300, shareButton.GetPosition().Y));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NRunHistory))]
        [HarmonyPatch("DisplayRun")]
        private static void BeforeDisplayRun(RunHistory history)
        {
            _currentRun = history;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NRunHistoryPlayerIcon))]
        [HarmonyPatch("Select")]
        private static void BeforeSelectPlayer(NRunHistoryPlayerIcon __instance)
        {
            _currentPlayer = __instance.Player;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(EventModel))]
        [HarmonyPatch("Node", MethodType.Getter)]
        private static bool BeforeNodeGet(ref Control? __result)
        {
            __result = _myEventNode;
            return _useDefaultNodeStuff;
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(EventModel))]
        [HarmonyPatch("Node", MethodType.Setter)]
        private static bool BeforeNodeSet(Control? node)
        {
            _myEventNode = node;
            return _useDefaultNodeStuff;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AncientEventModel))]
        [HarmonyPatch("Done")]
        private static void AfterDone()
        {
            _useDefaultNodeStuff = true;
        }
    }
}