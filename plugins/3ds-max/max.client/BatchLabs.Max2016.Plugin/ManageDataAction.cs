﻿
using BatchLabs.Max2016.Plugin.Common;
using BatchLabs.Max2016.Plugin.Max;
using BatchLabs.Plugin.Common.Code;
using BatchLabs.Plugin.Common.Resources;

namespace BatchLabs.Max2016.Plugin
{
    public class ManageDataAction : ActionBase
    {
        public override void InternalExecute()
        {            
            MaxGlobalInterface.Instance.COREInterface16.PushPrompt(Strings.ManageData_Log);
            Log.Instance.Debug(Strings.ManageData_Log);

            LabsRequestHandler.CallBatchLabs(Constants.BatchLabsUrs.Data);
        }

        public override string InternalActionText => Strings.ManageData_ActionText;
    }
}
