﻿
using BatchLabs.Max2019.Plugin.Common;
using BatchLabs.Max2019.Plugin.Max;
using BatchLabs.Plugin.Common.Code;
using BatchLabs.Plugin.Common.Resources;

namespace BatchLabs.Max2019.Plugin
{
    public class MonitorPoolsAction : ActionBase
    {
        public override void InternalExecute()
        {
            MaxGlobalInterface.Instance.COREInterface16.PushPrompt(Strings.MonitorPools_Log);
            Log.Instance.Debug(Strings.MonitorPools_Log);

            LabsRequestHandler.CallBatchLabs(Constants.BatchLabsUrs.Pools);
        }

        public override string InternalActionText => Strings.MonitorPools_ActionText;
    }
}
