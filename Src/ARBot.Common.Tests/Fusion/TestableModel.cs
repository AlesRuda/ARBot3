using ARBot.Common.Fusion;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Testovaci podtRIda zpRIstupNujici chranene haky modelu pro overeni matematiky.
    /// </summary>
    public class TestableModel : EKFModel
    {
        public TestableModel(FusionConfig cfg = null) : base(cfg) { }

        public Vector<double> PublicPredict(Vector<double> x, double dt) => PredictState(x, dt);
        public Matrix<double> PublicJacobianF(Vector<double> x, double dt) => JacobianF(x, dt);
        public Matrix<double> PublicProcessNoise(Vector<double> x, double dt) => ProcessNoise(x, dt);

        public static Vector<double> State(double x, double y, double th, double v, double w)
            => Vector<double>.Build.DenseOfArray(new[] { x, y, th, v, w });
    }
}
