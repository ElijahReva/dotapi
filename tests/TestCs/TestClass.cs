namespace TestNamespace
{
    public interface ITestInterface<out TOut, in TIn>
    {
        TOut TestTOut(TIn testTin);
    }
    
    public class TestClass: ITestInterface<string, long>
    {
        public string TestTOut(long testTin)
        {
            throw new System.NotImplementedException();
        }
        
        public static void TestStatic()
        {
            
        }
    }
}