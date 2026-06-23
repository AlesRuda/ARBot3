using ARBot.Common.Common;
using ARBot.Common.Logs;
using System.IO;

namespace ARBot.Common.Models
{
    public interface IEKFStepInfo
    {
        Matrix C { get; }
        Matrix CorrectedP { get; }
        Matrix CurrentState { get; }
        Matrix Diff { get; }
        Matrix FilteredOutput { get; }
        int Index { get; }
        Matrix Input { get; }
        string[] InputDescriptions { get; }
        Matrix K { get; }
        Matrix M { get; }
        string[] MeasurementDescriptions { get; }
        Matrix Output { get; }
        Matrix PredictedP { get; }
        Matrix PredictedState { get; }
        Matrix PrevP { get; }
        Matrix PrevState { get; }
        Matrix Q { get; }
        Matrix R { get; }
        Matrix R_Internal { get; }
        string[] StateDescriptions { get; }

    }
}