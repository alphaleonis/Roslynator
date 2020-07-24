// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Roslynator.CSharp.CodeFixes;
using Roslynator.CSharp.Testing;
using Xunit;

namespace Roslynator.CSharp.Analysis.Tests
{
    public class RCS1247UseNullCoalescingAssignmentOperatorTests : AbstractCSharpFixVerifier
    {
        public override DiagnosticDescriptor Descriptor { get; } = DiagnosticDescriptors.UseNullCoalescingAssignmentOperator;

        public override DiagnosticAnalyzer Analyzer { get; } = new UseNullCoalescingAssignmentOperatorAnalyzer();

        public override CodeFixProvider FixProvider { get; } = new CoalesceExpressionCodeFixProvider();

        [Fact, Trait(Traits.Analyzer, DiagnosticIdentifiers.UseNullCoalescingAssignmentOperator)]
        public async Task Test()
        {
            await VerifyDiagnosticAndFixAsync(@"
class C
{
    string M()
    {
        string x = null;

        return [|x ?? (x = M())|]; // x
    }
}
", @"
class C
{
    string M()
    {
        string x = null;

        return x ??= M(); // x
    }
}
");
        }

        [Fact, Trait(Traits.Analyzer, DiagnosticIdentifiers.UseNullCoalescingAssignmentOperator)]
        public async Task TestNoDiagnostic_ExpressionsAreNotEquivalent()
        {
            await VerifyNoDiagnosticAsync(@"
class C
{
    string M()
    {
        string x = null;
        string x2 = null;

        return x ?? (x2 = M());
    }
}
", options: CSharpCodeVerificationOptions.Default_CSharp7_3);
        }

        [Fact, Trait(Traits.Analyzer, DiagnosticIdentifiers.UseNullCoalescingAssignmentOperator)]
        public async Task TestNoDiagnostic_CSharp7_3()
        {
            await VerifyNoDiagnosticAsync(@"
class C
{
    string M()
    {
        string x = null;

        return x ?? (x = M());
    }
}
", options: CSharpCodeVerificationOptions.Default_CSharp7_3);
        }
    }
}
