﻿namespace OpcPlc.DeterministicAlarms.Model
{
    using Opc.Ua;
    using OpcPlc.DeterministicAlarms.Configuration;
    using OpcPlc.DeterministicAlarms.SimBackend;
    using System;
    using System.Collections.Generic;
    using System.Text;

    public class SimSourceNodeState : BaseObjectState
    {
        private readonly DeterministicAlarmsNodeManager _nodeManager;
        private readonly SimSourceNodeBackend _simSourceNodeBackend;
        private readonly Dictionary<string, ConditionState> _alarmNodes = new Dictionary<string, ConditionState>();
        private readonly Dictionary<string, ConditionState> _events = new Dictionary<string, ConditionState>();

        public SimSourceNodeState(DeterministicAlarmsNodeManager nodeManager, NodeId nodeId, string name, List<Alarm> alarms) : base(null)
        {
            _nodeManager = nodeManager;

            Initialize(_nodeManager.SystemContext);

            // Creates the whole backend object model for one source
            _simSourceNodeBackend = ((SimBackendService)_nodeManager.SystemContext.SystemHandle)
                .CreateSourceNodeBackend(name, alarms, OnAlarmChanged);

            // initialize the area with the fixed metadata.
            SymbolicName = name;
            NodeId = nodeId;
            BrowseName = new QualifiedName(name, nodeId.NamespaceIndex);
            DisplayName = BrowseName.Name;
            Description = null;
            ReferenceTypeId = null;
            TypeDefinitionId = ObjectTypeIds.BaseObjectType;
            EventNotifier = EventNotifiers.None;

            // This is to create all alarms
            _simSourceNodeBackend.Refresh();
        }

        public override void ConditionRefresh(ISystemContext context, List<IFilterTarget> events, bool includeChildren)
        {
            foreach (var @event in events)
            {
                var instanceSnapShotForExistingEvent = @event as InstanceStateSnapshot;
                if (instanceSnapShotForExistingEvent != null && Object.ReferenceEquals(instanceSnapShotForExistingEvent.Handle, this))
                {
                    return;
                }
            }

            foreach (var alarm in _alarmNodes.Values)
            {
                if (!alarm.Retain.Value)
                {
                    continue;
                }

                var instanceStateSnapshotNewAlarm = new InstanceStateSnapshot();
                instanceStateSnapshotNewAlarm.Initialize(context, alarm);
                instanceStateSnapshotNewAlarm.Handle = this;
                events.Add(instanceStateSnapshotNewAlarm);
            }
        }

        private void OnAlarmChanged(SimAlarmStateBackend alarm)
        {
            UpdateAlarmInSource(alarm);
        }

        public void UpdateAlarmInSource(SimAlarmStateBackend alarm, string eventId = null)
        {
            lock (_nodeManager.Lock)
            {
                if (!_alarmNodes.TryGetValue(alarm.Name, out ConditionState node))
                {
                    _alarmNodes[alarm.Name] = node = CreateAlarmOrCondition(alarm, null);
                }

                UpdateAlarm(node, alarm, eventId);
                ReportChanges(node);
            }
        }

        private ConditionState CreateAlarmOrCondition(SimAlarmStateBackend alarm, NodeId branchId)
        {
            ISystemContext context = _nodeManager.SystemContext;

            ConditionState node;

            // Condition
            if (alarm.AlarmType == AlarmObjectStates.ConditionType)
            {
                node = new ConditionState(this);
            }
            // All alarms inherent from AlarmConditionState
            else
            {
                switch (alarm.AlarmType)
                {
                    case AlarmObjectStates.TripAlarmType:
                        node = new TripAlarmState(this);
                        break;
                    case AlarmObjectStates.LimitAlarmType:
                        node = new LimitAlarmState(this);
                        break;
                    case AlarmObjectStates.OffNormalAlarmType:
                        node = new OffNormalAlarmState(this);
                        break;
                    default:
                        node = new AlarmConditionState(this);
                        break;
                }

                // create elements that conditiontype doesn't have
                CreateAlarmSpecificElements(context, (AlarmConditionState)node, branchId);
            }

            CreateCommonFieldsForAlarmAndCondition(context, node, alarm, branchId);

            // This call initializes the condition from the type model (i.e. creates all of the objects
            // and variables requried to store its state). The information about the type model was 
            // incorporated into the class when the class was created.
            //
            // This method also assigns new NodeIds to all of the components by calling the INodeIdFactory.New
            // method on the INodeIdFactory object which is part of the system context. The NodeManager provides
            // the INodeIdFactory implementation used here.
            node.Create(
                context,
                null,
                new QualifiedName(alarm.Name, BrowseName.NamespaceIndex),
                null,
                true);

            // initialize event information.node
            node.EventType.Value = node.TypeDefinitionId;
            node.SourceNode.Value = NodeId;
            node.SourceName.Value = SymbolicName;
            node.ConditionName.Value = node.SymbolicName;
            node.Time.Value = DateTime.UtcNow;
            node.ReceiveTime.Value = node.Time.Value;
            node.BranchId.Value = branchId;

            // don't add branches to the address space.
            if (NodeId.IsNull(branchId))
            {
                AddChild(node);
            }

            return node;
        }

        private void CreateAlarmSpecificElements(ISystemContext context, AlarmConditionState node, NodeId branchId)
        {
            node.ConfirmedState = new TwoStateVariableState(node);
            node.Confirm = new AddCommentMethodState(node);

            if (NodeId.IsNull(branchId))
            {
                node.SuppressedState = new TwoStateVariableState(node);
                node.ShelvingState = new ShelvedStateMachineState(node);
            }

            node.ActiveState = new TwoStateVariableState(node);
            node.ActiveState.TransitionTime = new PropertyState<DateTime>(node.ActiveState);
            node.ActiveState.EffectiveDisplayName = new PropertyState<LocalizedText>(node.ActiveState);
            node.ActiveState.Create(context, null, BrowseNames.ActiveState, null, false);
        }

        private void CreateCommonFieldsForAlarmAndCondition(ISystemContext context, ConditionState node, SimAlarmStateBackend alarm, NodeId branchId)
        {
            node.SymbolicName = alarm.Name;

            // add optional components.
            node.Comment = new ConditionVariableState<LocalizedText>(node);
            node.ClientUserId = new PropertyState<string>(node);
            node.AddComment = new AddCommentMethodState(node);

            // adding optional components to children is a little more complicated since the 
            // necessary initilization strings defined by the class that represents the child.
            // in this case we pre-create the child, add the optional components
            // and call create without assigning NodeIds. The NodeIds will be assigned when the
            // parent object is created.
            node.EnabledState = new TwoStateVariableState(node);
            node.EnabledState.TransitionTime = new PropertyState<DateTime>(node.EnabledState);
            node.EnabledState.EffectiveDisplayName = new PropertyState<LocalizedText>(node.EnabledState);
            node.EnabledState.Create(context, null, BrowseNames.EnabledState, null, false);

            // specify reference type between the source and the alarm.
            node.ReferenceTypeId = ReferenceTypeIds.HasComponent;
        }

        private void UpdateAlarm(ConditionState node, SimAlarmStateBackend alarm, string eventId = null)
        {
            ISystemContext context = _nodeManager.SystemContext;

            // remove old event.
            if (node.EventId.Value != null)
            {
                _events.Remove(Utils.ToHexString(node.EventId.Value));
            }

            node.EventId.Value = eventId != null ? Encoding.UTF8.GetBytes(eventId) : Guid.NewGuid().ToByteArray();
            node.Time.Value = DateTime.UtcNow;
            node.ReceiveTime.Value = node.Time.Value;

            // save the event for later lookup.
            _events[Utils.ToHexString(node.EventId.Value)] = node;

            // determine the retain state.
            node.Retain.Value = true;

            if (alarm != null)
            {
                node.Time.Value = alarm.Time;
                node.Message.Value = new LocalizedText(alarm.Reason);
                node.SetComment(context, alarm.Comment, alarm.UserName);
                node.SetSeverity(context, alarm.Severity);
                node.EnabledState.TransitionTime.Value = alarm.EnableTime;
                node.SetEnableState(context, (alarm.State & SimConditionStatesEnum.Enabled) != 0);

                if (node is AlarmConditionState)
                {
                    var nodeAlarm = (AlarmConditionState)node;
                    nodeAlarm.SetAcknowledgedState(context, (alarm.State & SimConditionStatesEnum.Acknowledged) != 0);
                    nodeAlarm.SetConfirmedState(context, (alarm.State & SimConditionStatesEnum.Confirmed) != 0);
                    nodeAlarm.SetActiveState(context, (alarm.State & SimConditionStatesEnum.Active) != 0);
                    nodeAlarm.SetSuppressedState(context, (alarm.State & SimConditionStatesEnum.Suppressed) != 0);
                    nodeAlarm.ActiveState.TransitionTime.Value = alarm.ActiveTime;
                    // not interested in inactive alarms
                    if (!nodeAlarm.ActiveState.Id.Value)
                    {
                        nodeAlarm.Retain.Value = false;
                    }
                }
            }

            // check for deleted items.
            if ((alarm.State & SimConditionStatesEnum.Deleted) != 0)
            {
                node.Retain.Value = false;
            }

            // not interested in disabled alarms.
            if (!node.EnabledState.Id.Value)
            {
                node.Retain.Value = false;
            }
        }

        private void ReportChanges(ConditionState alarm)
        {
            // report changes to node attributes.
            alarm.ClearChangeMasks(_nodeManager.SystemContext, true);

            // check if events are being monitored for the source.
            if (AreEventsMonitored)
            {
                // create a snapshot.
                var e = new InstanceStateSnapshot();
                e.Initialize(_nodeManager.SystemContext, alarm);

                // report the event.
                alarm.ReportEvent(_nodeManager.SystemContext, e);
            }
        }
    }
}