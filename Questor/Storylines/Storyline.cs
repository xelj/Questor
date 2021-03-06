﻿namespace Questor.Storylines
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DirectEve;
    using global::Questor.Modules;

    public class Storyline
    {
        public StorylineState State { get; set; }

        public long CurrentStorylineAgentId { get; private set; }
        
        private IStoryline _storyline;
        private readonly Dictionary<string, IStoryline> _storylines;
        private readonly List<long> _agentBlacklist;

        private readonly Combat _combat;
        private readonly Traveler _traveler;
        private readonly AgentInteraction _agentInteraction;

        private DateTime _nextAction = DateTime.MinValue;
        private DateTime _nextStoryLineAttempt = DateTime.MinValue;

        public Storyline()
        {
            _combat = new Combat();
            _traveler = new Traveler();
            _agentInteraction = new AgentInteraction();

            _agentBlacklist = new List<long>();

            _storylines = new Dictionary<string, IStoryline>
                {
                    //{"__", new GenericCombatStoryline()}, //example
                    {"Materials For War Preparation", new MaterialsForWarPreparation()},
                    {"Shipyard Theft", new GenericCombatStoryline()},
                    {"Evolution", new GenericCombatStoryline()},
                    {"Record Cleaning", new GenericCombatStoryline()},
                    {"Covering Your Tracks", new GenericCombatStoryline()},
                    {"Crowd Control", new GenericCombatStoryline()},
                    {"A Force to Be Reckoned With", new GenericCombatStoryline()},
                    {"Kidnappers Strike - Ambush In The Dark (1 of 10)", new GenericCombatStoryline()},
                    {"Kidnappers Strike - The Kidnapping (3 of 10)", new GenericCombatStoryline()},
                    {"Kidnappers Strike - Incriminating Evidence (5 of 10)", new GenericCombatStoryline()},
                    {"Kidnappers Strike - The Secret Meeting (7 of 10)", new GenericCombatStoryline()},
                    {"Kidnappers Strike - Defend the Civilian Convoy (8 of 10)", new GenericCombatStoryline()},
                    {"Kidnappers Strike - Retrieve the Prisoners (9 of 10)", new GenericCombatStoryline()},
                    {"Kidnappers Strike - The Final Battle (10 of 10)", new GenericCombatStoryline()},
                    {"Whispers in the Dark - First Contact (1 of 4)", new GenericCombatStoryline()},
                    {"Whispers in the Dark - Lay and Pray (2 of 4)", new GenericCombatStoryline()},
                    {"Whispers in the Dark - The Outpost (4 of 4)", new GenericCombatStoryline()},
                    {"Transaction Data Delivery", new TransactionDataDelivery()},
                    {"Innocents in the Crossfire", new GenericCombatStoryline()},
                    {"Patient Zero", new GenericCombatStoryline()},
                    {"Soothe the Salvage Beast", new GenericCombatStoryline()},
                    {"Forgotten Outpost", new GenericCombatStoryline()},
                    {"Stem the Flow", new GenericCombatStoryline()},
                    {"Nine Tenths of the Wormhole", new GenericCombatStoryline()},
                    {"Blood Farm", new GenericCombatStoryline()}, 
                    {"Jealous Rivals", new GenericCombatStoryline()},
                    //{"Quota Season", new GenericCombatStoryline()}, //why cant we pickup the Custom Circuitry? problem in salvage.cs somewhere: "salvage: Container" name: Number "contained no valuable loot"
                    //{"Matriarch", new GenericCombatStoryline()},
                    //{"Diplomatic Incident", new GenericCombatStoryline()},
                    //these work but are against other factions that I generally like to avoid
                    //{"The Blood of Angry Men", new GenericCombatStoryline()},  //amarr faction
                    //{"Amarrian Excavators", new GenericCombatStoryline()}, 	//amarr faction
                };
        }

        public void Reset()
        {
            State = StorylineState.Idle;
            CurrentStorylineAgentId = 0;
            _storyline = null;
            _agentInteraction.State = AgentInteractionState.Idle;
            _traveler.State = TravelerState.Idle;
            _traveler.Destination = null;
        }

        private DirectAgentMission StorylineMission
        {
            get
            {
                IEnumerable<DirectAgentMission> missionsinjournal = Cache.Instance.DirectEve.AgentMissions;
                if (CurrentStorylineAgentId != 0)
                    return missionsinjournal.FirstOrDefault(m => m.AgentId == CurrentStorylineAgentId);

                missionsinjournal = missionsinjournal.Where(m => !_agentBlacklist.Contains(m.AgentId));
                missionsinjournal = missionsinjournal.Where(m => m.Important);
                missionsinjournal = missionsinjournal.Where(m => _storylines.ContainsKey(Cache.Instance.FilterPath(m.Name)));
                missionsinjournal = missionsinjournal.Where(m => !Settings.Instance.MissionBlacklist.Any(b => b.ToLower() == Cache.Instance.FilterPath(m.Name).ToLower()));
                //missions = missions.Where(m => !Settings.Instance.MissionGreylist.Any(b => b.ToLower() == Cache.Instance.FilterPath(m.Name).ToLower()));
                return missionsinjournal.FirstOrDefault();
            }
        }

        private void IdleState()
        {
            DirectAgentMission currentStorylineMission = StorylineMission;
            if (currentStorylineMission == null)
            {
                _nextStoryLineAttempt = DateTime.Now.AddMinutes(15);
                State = StorylineState.Done;
                Cache.Instance.MissionName = String.Empty;
                return;
            }

            CurrentStorylineAgentId = currentStorylineMission.AgentId;
            DirectAgent storylineagent = Cache.Instance.DirectEve.GetAgentById(CurrentStorylineAgentId);
            if (storylineagent == null)
            {
                Logging.Log("Storyline: Unknown agent [" + CurrentStorylineAgentId + "]");

                State = StorylineState.Done;
                return;
            }

            Logging.Log("Storyline: Going to do [" + currentStorylineMission.Name + "] for agent [" + storylineagent.Name + "]");
            Cache.Instance.MissionName = currentStorylineMission.Name;

            State = StorylineState.Arm;
            _storyline = _storylines[Cache.Instance.FilterPath(currentStorylineMission.Name)];
        }

        private void GotoAgent(StorylineState nextState)
        {
            DirectAgent storylineagent = Cache.Instance.DirectEve.GetAgentById(CurrentStorylineAgentId);
            if (storylineagent == null)
            {
                State = StorylineState.Done;
                return;
            }

            var baseDestination = _traveler.Destination as StationDestination;
            if (baseDestination == null || baseDestination.StationId != storylineagent.StationId)
                _traveler.Destination = new StationDestination(storylineagent.SolarSystemId, storylineagent.StationId, Cache.Instance.DirectEve.GetLocationName(storylineagent.StationId));

            if (Cache.Instance.PriorityTargets.Any(pt => pt != null && pt.IsValid))
            {
                Logging.Log("Storyline: GotoAgent: Priority targets found, engaging!");
                _combat.ProcessState();
            }

            _traveler.ProcessState();
            if (_traveler.State == TravelerState.AtDestination)
            {
                State = nextState;
                _traveler.Destination = null;
            }

            if (Settings.Instance.DebugStates)
                Logging.Log("Traveler.State = " + _traveler.State);
        }

        private void BringSpoilsOfWar()
        {
            DirectEve directEve = Cache.Instance.DirectEve;
            if (_nextAction > DateTime.Now)
                return;

            // Open the item hangar (should still be open)
            if (!Cache.Instance.OpenItemsHangar("Storyline")) return;

            // Do we have any implants?
            if (!Cache.Instance.ItemHangar.Items.Any(i => i.GroupId >= 738 && i.GroupId <= 750))
            {
                State = StorylineState.Done;
                return;
            }

            // Yes, open the ships cargo
            if (!Cache.Instance.OpenCargoHold("Storyline")) return;

            // If we aren't moving items
            if (Cache.Instance.DirectEve.GetLockedItems().Count == 0)
            {
                // Move all the implants to the cargo bay
                foreach (DirectItem item in Cache.Instance.ItemHangar.Items.Where(i => i.GroupId >= 738 && i.GroupId <= 750))
                {
                    if (Cache.Instance.CargoHold.Capacity - Cache.Instance.CargoHold.UsedCapacity - (item.Volume * item.Quantity) < 0)
                    {
                        Logging.Log("Storyline: We are full, not moving anything else");
                        State = StorylineState.Done;
                        return;
                    }

                    Logging.Log("Storyline: Moving [" + item.TypeName + "][" + item.ItemId + "] to cargo");
                    Cache.Instance.CargoHold.Add(item, item.Quantity);
                }
                _nextAction = DateTime.Now.AddSeconds(10);
            }
            return;
        }

        public void ProcessState()
        {
            switch (State)
            {
                case StorylineState.Idle:
                    IdleState();
                    break;

                case StorylineState.Arm:
                    State = _storyline.Arm(this);
                    break;

                case StorylineState.GotoAgent:
                    GotoAgent(StorylineState.PreAcceptMission);
                    break;

                case StorylineState.PreAcceptMission:
                    State = _storyline.PreAcceptMission(this);
                    break;

                case StorylineState.AcceptMission:
                    if (_agentInteraction.State == AgentInteractionState.Idle)
                    {
                        Logging.Log("AgentInteraction: Start conversation [Start Mission]");

                        _agentInteraction.State = AgentInteractionState.StartConversation;
                        _agentInteraction.Purpose = AgentInteractionPurpose.StartMission;
                        _agentInteraction.AgentId = CurrentStorylineAgentId;
                        _agentInteraction.ForceAccept = true;
                    }

                    _agentInteraction.ProcessState();

                    if (Settings.Instance.DebugStates)
                        Logging.Log("AgentInteraction.State = " + _agentInteraction.State);

                    if (_agentInteraction.State == AgentInteractionState.Done)
                    {
                        _agentInteraction.State = AgentInteractionState.Idle;
                        // If there is no mission anymore then we're done (we declined it)
                        State = StorylineMission == null ? StorylineState.Done : StorylineState.ExecuteMission;
                    }
                    break;
                
                case StorylineState.ExecuteMission:
                    State = _storyline.ExecuteMission(this);
                    break;

                case StorylineState.ReturnToAgent:
                    GotoAgent(StorylineState.CompleteMission);
                    break;

                case StorylineState.CompleteMission:
                    if (_agentInteraction.State == AgentInteractionState.Idle)
                    {
                        Logging.Log("AgentInteraction: Start Conversation [Complete Mission]");

                        _agentInteraction.State = AgentInteractionState.StartConversation;
                        _agentInteraction.Purpose = AgentInteractionPurpose.CompleteMission;
                    }

                    _agentInteraction.ProcessState();

                    if (Settings.Instance.DebugStates)
                        Logging.Log("AgentInteraction.State = " + _agentInteraction.State);

                    if (_agentInteraction.State == AgentInteractionState.Done)
                    {
                        _agentInteraction.State = AgentInteractionState.Idle;
                        State = StorylineState.BringSpoilsOfWar;
                    }
                    break;

                case StorylineState.BringSpoilsOfWar:
                    BringSpoilsOfWar();
                    break;

                case StorylineState.BlacklistAgent:
                    _agentBlacklist.Add(CurrentStorylineAgentId);
                    State = StorylineState.Done;
                    break;

                case StorylineState.Done:
                    if (DateTime.Now > _nextStoryLineAttempt)
                    {
                        State = StorylineState.Idle;
                    }
                    break;
            }
        }

        public bool HasStoryline()
        {
            // Do we have a registered storyline?
            return StorylineMission != null;
        }

        public IStoryline StorylineHandler
        {
            get { return _storyline; }
        }
    }
}
