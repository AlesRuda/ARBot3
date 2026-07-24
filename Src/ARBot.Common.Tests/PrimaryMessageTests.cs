using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Tests
{
    /// <summary>Testy markeru <see cref="IPrimaryMessage"/> na senzorovych merenich.</summary>
    public class PrimaryMessageTests
    {
        [Test]
        public void ImuState_IsPrimaryMessage()
        {
            Assert.That(new IMUState() is IPrimaryMessage, Is.True);
        }

        [Test]
        public void RobotStateMsg_IsNotPrimaryMessage()
        {
            // Odvozena zprava nesmi nest marker primarniho vstupu.
            Assert.That(new RobotStateMsg() is IPrimaryMessage, Is.False);
        }
    }
}
