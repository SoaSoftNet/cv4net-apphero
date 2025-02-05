﻿/*
 * SPDX-FileCopyrightText: Copyright Corsinvest Srl
 * SPDX-License-Identifier: AGPL-3.0-only
 */
using Corsinvest.AppHero.Core.Modularity;
using FluentResults;

namespace Corsinvest.AppHero.Core.Translator;

public interface ITranslator : IGroupableService
{
    string IGroupableService.GetGroupName() => "Translator";
    string IGroupableService.GetGroupIcon() => UI.UIIcon.Translate.GetName();

    Task<IResult<IEnumerable<string>>> TranslateAsync(IServiceScope scope, string source, string targets, IEnumerable<string> texts);
}