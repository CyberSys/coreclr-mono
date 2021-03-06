using System;
using System.Globalization;
using TestLibrary;

class CompareInfoIsSuffix
{
    static int Main()
    {
        CompareInfoIsSuffix test = new CompareInfoIsSuffix();

        TestFramework.BeginTestCase("CompareInfo.IsSuffix");

        if (test.RunTests())
        {
            TestFramework.EndTestCase();
            TestFramework.LogInformation("PASS");
            return 100;
        }
        else
        {
            TestFramework.EndTestCase();
            TestFramework.LogInformation("FAIL");
            return 0;
        }

    }

    public bool RunTests()
    {
        bool ret = true;

        ret &= Test1();
        ret &= Test2();
        ret &= Test3();
        ret &= Test4();
        ret &= Test5();
        ret &= Test6();
        ret &= Test7();
        ret &= Test8();
        ret &= Test9();
        ret &= Test11();
		if (!TestLibrary.Utilities.IsVista) ret &= Test12(); // Disabled by Windows 7 Bug #137775
        ret &= Test13();
        ret &= Test14();
        ret &= Test15();
        ret &= Test16();
        ret &= Test17();
        ret &= Test18();
        ret &= Test19();
        ret &= Test20();
        ret &= Test21();
        ret &= Test22();
        ret &= Test23();
        ret &= Test24();
        ret &= Test25();
        ret &= Test26();
        ret &= Test27();
        ret &= Test28();
        ret &= Test29();
        ret &= Test30();
        ret &= Test31();
        ret &= Test32();
        ret &= Test33();
        ret &= Test34();
        ret &= Test35();

        return ret;
    }

    static CultureInfo hungarian = new CultureInfo("hu-HU");
    static CultureInfo turkish = new CultureInfo("tr-TR");

    public bool Test1() { return TestExc(CultureInfo.InvariantCulture, null, "Test", typeof(ArgumentNullException), CompareOptions.None, "001"); }
    public bool Test2() { return TestExc(CultureInfo.InvariantCulture, "Test", null, typeof(ArgumentNullException), CompareOptions.None, "002"); }
    public bool Test3() { return TestExc(CultureInfo.InvariantCulture, null, null, typeof(ArgumentNullException), CompareOptions.None, "003"); }
    public bool Test4() { return TestExc(CultureInfo.InvariantCulture, "Test's", "Tests", typeof(ArgumentException), CompareOptions.StringSort, "004"); }
    public bool Test5() { return TestExc(CultureInfo.InvariantCulture, "Test's", "Tests", typeof(ArgumentException), (CompareOptions)(-1), "004"); }
    public bool Test6() { return TestExc(CultureInfo.InvariantCulture, "Test's", "Tests", typeof(ArgumentException), (CompareOptions)0x11111111, "006"); }

    public bool Test7() { return Test(CultureInfo.InvariantCulture, "foo", "", true, CompareOptions.None, "007"); }
    public bool Test8() { return Test(CultureInfo.InvariantCulture, "", "", true, CompareOptions.None, "008"); }

    public bool Test9() { return Test(CultureInfo.InvariantCulture, new string('a', 5555), "aaaaaaaaaaaaaaa", true, CompareOptions.None, "009"); }
    public bool Test10() { return Test(CultureInfo.InvariantCulture, new string('a', 5555), new string('a', 5000), true, CompareOptions.None, "010"); }
    public bool Test11() { return Test(CultureInfo.InvariantCulture, new string('a', 5555), new string('a', 5000) + "b", false, CompareOptions.None, "011"); }

    public bool Test12() { return Test(hungarian, "foobardzsdzs", "rddzs", true, CompareOptions.None, "064"); }
    public bool Test13() { return TestOrd(hungarian, "foobardzsdzs", "rddzs", false, CompareOptions.Ordinal, "065"); }
    public bool Test14() { return Test(CultureInfo.InvariantCulture, "foobardzsdzs", "rddzs", false, CompareOptions.None, "066"); }
    public bool Test15() { return TestOrd(CultureInfo.InvariantCulture, "foobardzsdzs", "rddzs", false, CompareOptions.Ordinal, "067"); }

    public bool Test16() { return Test(turkish, "Hi", "I", false, CompareOptions.IgnoreCase, "068s"); }
    public bool Test17() { return Test(CultureInfo.InvariantCulture, "Hi", "I", true, CompareOptions.IgnoreCase, "069"); }
    public bool Test18() { return Test(turkish, "Hi", "\u0130", true, CompareOptions.IgnoreCase, "070s"); }
    public bool Test19() { return Test(CultureInfo.InvariantCulture, "Hi", "\u0130", false, CompareOptions.IgnoreCase, "071"); }
    public bool Test20() { return Test(turkish, "Hi", "I", false, CompareOptions.None, "072"); }
    public bool Test21() { return Test(CultureInfo.InvariantCulture, "Hi", "I", false, CompareOptions.None, "073"); }
    public bool Test22() { return Test(turkish, "Hi", "\u0130", false, CompareOptions.None, "074"); }
    public bool Test23() { return Test(CultureInfo.InvariantCulture, "Hi", "\u0130", false, CompareOptions.None, "075"); }

    public bool Test24() { return Test(CultureInfo.InvariantCulture, "Exhibit \u00C0", "A\u0300", true, CompareOptions.None, "076"); }
    public bool Test25() { return TestOrd(CultureInfo.InvariantCulture, "Exhibit \u00C0", "A\u0300", false, CompareOptions.Ordinal, "077"); }
    public bool Test26() { return Test(CultureInfo.InvariantCulture, "Exhibit \u00C0", "a\u0300", true, CompareOptions.IgnoreCase, "078"); }
    public bool Test27() { return TestOrd(CultureInfo.InvariantCulture, "Exhibit \u00C0", "a\u0300", false, CompareOptions.OrdinalIgnoreCase, "079"); }
    public bool Test28() { return Test(CultureInfo.InvariantCulture, "Exhibit \u00C0", "a\u0300", false, CompareOptions.None, "080"); }
    public bool Test29() { return TestOrd(CultureInfo.InvariantCulture, "Exhibit \u00C0", "a\u0300", false, CompareOptions.Ordinal, "081"); }

    public bool Test30() { return Test(CultureInfo.InvariantCulture, "FooBar", "Foo\u0400Bar", true, CompareOptions.None, "082"); }
    public bool Test31() { return TestOrd(CultureInfo.InvariantCulture, "FooBar", "Foo\u0400Bar", false, CompareOptions.Ordinal, "083"); }
    public bool Test32() { return Test(CultureInfo.InvariantCulture, "FooBar", "Foo\u0400Bar", true, CompareOptions.IgnoreNonSpace, "084"); }
    public bool Test33() { return Test(CultureInfo.InvariantCulture, "FooBA\u0300R", "Foo\u0400B\u00C0R", true, CompareOptions.IgnoreNonSpace, "085"); }

    public bool Test34() { return Test(CultureInfo.InvariantCulture, "More Test's", "Tests", true, CompareOptions.IgnoreSymbols, "086"); }
    public bool Test35() { return Test(CultureInfo.InvariantCulture, "More Test's", "Tests", false, CompareOptions.None, "087"); }

    public bool Test(CultureInfo culture, string str1, string str2, bool expected, CompareOptions options, string id)
    {
		if (!id.Contains("s") || !Utilities.IsVistaOrLater) //Due Windows 7 bug 130925
			expected = GlobLocHelper.OSIsSuffix(culture, str1, str2, options);
        CompareInfo ci = culture.CompareInfo;
        bool result = true;
        if (str1 == null || str2 == null || (str1.Length < 100 && str2.Length < 100))
            TestFramework.BeginScenario(id + ": Comparing " + ((str1 == null) ? "null" : str1) + " / " + ((str2 == null) ? "null" : str2) + "; options: " + options + "; culture: " + ci.Name);
        else
            TestFramework.BeginScenario(id + ": Comparing LongStr (" + str1.Length + ") / LongStr(" + str2.Length + "); options: " + options + "; culture: " + ci.Name);
        try
        {
            bool i = ci.IsSuffix(str1, str2, options);
            if (i != expected)
            {
                result = false;
                TestFramework.LogError("001", "Error in " + id + ", unexpected comparison result. Actual: " + i + ", Expected: " + expected);
            }
        }
        catch (Exception exc)
        {
            result = false;
            TestFramework.LogError("003", "Unexpected exception in " + id + ", excpetion: " + exc.ToString());
        }
        return result;
    }

    public bool TestOrd(CultureInfo culture, string str1, string str2, bool expected, CompareOptions options, string id)
    {
        CompareInfo ci = culture.CompareInfo;
        bool result = true;
        if (str1 == null || str2 == null || (str1.Length < 100 && str2.Length < 100))
            TestFramework.BeginScenario(id + ": Comparing " + ((str1 == null) ? "null" : str1) + " / " + ((str2 == null) ? "null" : str2) + "; options: " + options + "; culture: " + ci.Name);
        else
            TestFramework.BeginScenario(id + ": Comparing LongStr (" + str1.Length + ") / LongStr(" + str2.Length + "); options: " + options + "; culture: " + ci.Name);
        try
        {
            bool i = ci.IsSuffix(str1, str2, options);
            if (i != expected)
            {
                result = false;
                TestFramework.LogError("001", "Error in " + id + ", unexpected comparison result. Actual: " + i + ", Expected: " + expected);
            }
        }
        catch (Exception exc)
        {
            result = false;
            TestFramework.LogError("003", "Unexpected exception in " + id + ", excpetion: " + exc.ToString());
        }
        return result;
    }

    public bool TestExc(CultureInfo culture, string str1, string str2, Type expected, CompareOptions options, string id)
    {
        CompareInfo ci = culture.CompareInfo;
        bool result = true;
        TestFramework.BeginScenario(id + ": Comparing " + str1 + " / " + str2 + "; options: " + options + "; culture: " + ci.Name);
        try
        {
            bool i = ci.IsSuffix(str1, str2, options);
            result = false;
            TestFramework.LogError("004", "Error in " + id + ", expected exception did not occur. Comparison result: " + i);
        }
        catch (Exception exc)
        {
            if (!exc.GetType().Equals(expected))
            {
                result = false;
                TestFramework.LogError("005", "Unexpected exception in " + id + ", excpetion: " + exc.ToString());
            }
        }
        return result;
    }
}