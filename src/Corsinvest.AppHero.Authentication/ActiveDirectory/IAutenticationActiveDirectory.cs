﻿/*
 * SPDX-FileCopyrightText: Copyright Corsinvest Srl
 * SPDX-License-Identifier: AGPL-3.0-only
 */
using FluentResults;

namespace Corsinvest.AppHero.Authentication.ActiveDirectory;

public interface IAutenticationActiveDirectory 
{
    Task<IResult<bool>> LoginAsync(LoginRequestAD loginRequestAD);
}