﻿// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Roslynator.CSharp.Analysis
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class UseExplicitTypeInsteadOfVarWhenTypeIsNotObviousAnalyzer : BaseDiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(DiagnosticDescriptors.UseExplicitTypeInsteadOfVarWhenTypeIsNotObvious); }
        }

        public override void Initialize(AnalysisContext context)
        {
            base.Initialize(context);

            context.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeDeclarationExpression, SyntaxKind.DeclarationExpression);
        }

        private static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
        {
            var variableDeclaration = (VariableDeclarationSyntax)context.Node;

            if (CSharpTypeAnalysis.IsImplicitThatCanBeExplicit(variableDeclaration, context.SemanticModel, TypeAppearance.NotObvious, context.CancellationToken))
            {
                DiagnosticHelpers.ReportDiagnostic(context,
                    DiagnosticDescriptors.UseExplicitTypeInsteadOfVarWhenTypeIsNotObvious,
                    variableDeclaration.Type);
            }
        }

        private static void AnalyzeDeclarationExpression(SyntaxNodeAnalysisContext context)
        {
            var declarationExpression = (DeclarationExpressionSyntax)context.Node;

            if (CSharpTypeAnalysis.IsImplicitThatCanBeExplicit(declarationExpression, context.SemanticModel, context.CancellationToken))
            {
                DiagnosticHelpers.ReportDiagnostic(context,
                    DiagnosticDescriptors.UseExplicitTypeInsteadOfVarWhenTypeIsNotObvious,
                    declarationExpression.Type);
            }
        }
    }
}
