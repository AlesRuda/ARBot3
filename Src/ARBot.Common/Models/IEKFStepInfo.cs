using ARBot.Common.Logs;
using MathNet.Numerics.LinearAlgebra;
using System.IO;

namespace ARBot.Common.Models
{
    public interface IEKFStepInfo
    {
        Matrix<double> C { get; }
        Matrix<double> CorrectedP { get; }
        Matrix<double> CurrentState { get; }
        Matrix<double> Diff { get; }
        Matrix<double> FilteredOutput { get; }
        int Index { get; }
        Matrix<double> Input { get; }
        string[] InputDescriptions { get; }
        Matrix<double> K { get; }
        Matrix<double> M { get; }
        string[] MeasurementDescriptions { get; }
        Matrix<double> Output { get; }
        Matrix<double> PredictedP { get; }
        Matrix<double> PredictedState { get; }
        Matrix<double> PrevP { get; }
        Matrix<double> PrevState { get; }
        Matrix<double> Q { get; }
        Matrix<double> R { get; }
        Matrix<double> R_Internal { get; }
        string[] StateDescriptions { get; }

    }
}