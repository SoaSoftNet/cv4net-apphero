﻿/*
 * SPDX-FileCopyrightText: Copyright Corsinvest Srl
 * SPDX-License-Identifier: AGPL-3.0-only
 */
using BlazorDownloadFile;
using ClosedXML.Excel;
using Corsinvest.AppHero.Core.Security.Auth.Permissions;
using DocumentFormat.OpenXml.Office.CustomUI;
using MudBlazor;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;

namespace Corsinvest.AppHero.Core.MudBlazorUI.Shared.Components.DataGrid;

public class DataGridManager<T> : IDataGridManager<T> where T : class
{
    private readonly IDialogService _dialogService;
    private readonly IBlazorDownloadFileService _blazorDownloadFileService;
    private GridState<T> _state = default!;

    public DataGridManager(IBlazorDownloadFileService blazorDownloadFileService, IDialogService dialogService)
    {
        _blazorDownloadFileService = blazorDownloadFileService;
        _dialogService = dialogService;
        NewAsync = NewDefaultAsync;
    }

    #region Delete
    public async Task<bool> DeleteSelectedItemsAsync(bool askConfirm)
    {
        var items = DataGrid!.SelectedItems.ToArray();
        var ret = items.Length > 0 && await DeleteItemsAsync(items, askConfirm);
        if (ret) { DataGrid!.SelectedItems.Clear(); }
        return ret;
    }

    public async Task<bool> DeleteItemsAsync(IEnumerable<T> items, bool askConfirm)
    {
        var ret = false;
        if (!askConfirm || await _dialogService.DeleteAsync("Confirm delete selections?"))
        {
            if (await DeleteAsync(items))
            {
                if (DeleteAfterAsync != null) { await DeleteAfterAsync.Invoke(items); }

                ret = true;
                await Refresh();
            }
        }

        return ret;
    }

    public Func<IEnumerable<T>, Task<bool>> DeleteAsync { get; set; } = default!;
    public Func<IEnumerable<T>, Task> DeleteAfterAsync { get; set; } = default!;
    #endregion

    public async Task Refresh()
    {
        if (QueryAsync != null) { await DataGrid!.ReloadServerData(); }
    }

    #region New
    public virtual async Task NewDefaultAsync()
    {
        if (NewBeforeAsync != null) { await NewBeforeAsync.Invoke(); }
        await EditAsync(Activator.CreateInstance<T>(), true);
    }

    public Func<Task> NewAsync { get; set; } = default!;
    public Func<Task> NewBeforeAsync { get; set; } = default!;
    #endregion

    #region Edit
    public RenderFragment<T> EditTemplate { get; set; } = default!;

    public virtual async Task<bool> EditAsync(T item, bool isNew)
    {
        var editItem = await BeforeEditAsync(item, isNew);
        var ret = await _dialogService.ShowDialogEditorAsync(Title,
                                                        EditTemplate,
                                                        editItem,
                                                        isNew,
                                                        SaveAsync,
                                                        new DialogOptions
                                                        {
                                                            CloseButton = true,
                                                            MaxWidth = EditDialogMaxWidth,
                                                            FullWidth = true,
                                                            DisableBackdropClick = true
                                                        },
                                                        "Cancel",
                                                        "Save");

        if (ret) { await Refresh(); }
        return ret;
    }

    public Func<T, bool, Task<T>> BeforeEditAsync { get; set; } = async (item, isNew) => await Task.FromResult(item);
    public MaxWidth EditDialogMaxWidth { get; set; }
    #endregion

    #region Save
    public async Task<bool> SaveDefaultAsync(T item, bool isNew)
    {
        var ret = SaveBeforeAsync == null || await SaveBeforeAsync.Invoke(item, isNew);
        if (ret)
        {
            ret = await SaveAsync(item, isNew);
            if (ret && SaveAfterAsync != null) { await SaveAfterAsync.Invoke(item, isNew); }
        }
        return ret;
    }

    public Func<T, bool, Task<bool>> SaveAsync { get; set; } = default!;
    public Func<T, bool, Task<bool>> SaveBeforeAsync { get; set; } = default!;
    public Func<T, bool, Task> SaveAfterAsync { get; set; } = default!;
    #endregion

    public string Title { get; set; } = default!;
    public Dictionary<string, bool> DefaultSort { get; set; } = new();
    public Func<Task<IEnumerable<T>>> QueryAsync { get; set; } = default!;
    public MudDataGrid<T>? DataGrid { get; set; } = default!;

    private string _search = default!;
    public string Search
    {
        get => _search;
        set
        {
            _search = value;
            DataGrid!.ReloadServerData();
        }
    }

    public bool ExistsSelection => DataGrid!.SelectedItems.Any();

    public async Task ExportToExcel()
    {
        using var stream = new MemoryStream();
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add();

        var fields = new List<string>();
        var col = 'A';
        var row = 1;

        for (int i = 0; i < DataGrid!.RenderedColumns.Count; i++)
        {
            var column = DataGrid!.RenderedColumns[i];
            if (!string.IsNullOrWhiteSpace(column.PropertyName))
            {
                worksheet.Cell($"{col}1").Value = column.Title;
                fields.Add(column.PropertyName);
                col++;
            }
        }

        col--;
        worksheet.Range($"A1:{col}1").SetAutoFilter();
        row++;
        foreach (var item in (await DataGrid!.ServerData(_state)).Items)
        {
            col = 'A';
            foreach (var field in fields)
            {
                var pi = item!.GetType().GetPropertyFromPath(field);
                if (pi != null)
                {
                    var value = (pi.GetValue(item) + "").ToString();
                    worksheet.Cell($"{col}{row}").Value = value;
                    col++;
                }
            }
            row++;
        }

        var header = worksheet.Range($"A1:{--col}1");
        header.Style.Fill.SetBackgroundColor(XLColor.LightBlue);
        header.Style.Font.Bold = true;
        header.SetAutoFilter();

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(stream);
        stream.Seek(0, SeekOrigin.Begin);

        var fileName = string.IsNullOrEmpty(Title)
                        ? "data"
                        : Title;

        await _blazorDownloadFileService.DownloadFile($"{fileName}.xlsx", stream, "application/vnd.ms-excel");
    }

    public async Task<GridData<T>> LoadServerData(GridState<T> state)
    {
        _state = state;
        var query = (await QueryAsync()).AsQueryable();

        if (!string.IsNullOrWhiteSpace(_search))
        {
            //manual search
            var where = new List<string>();
            foreach (var item in DataGrid!.RenderedColumns.Where(a => (a.Filterable == true || DataGrid!.Filterable)
                                                                      && !string.IsNullOrWhiteSpace(a.PropertyName)))
            {
                var pi = typeof(T).GetProperty(item.PropertyName)!;
                if (pi != null && pi.CanWrite)
                {
                    var @operator = Type.GetTypeCode(pi.PropertyType) switch
                    {
                        TypeCode.Empty or TypeCode.Object or TypeCode.DBNull => string.Empty,

                        TypeCode.Boolean => bool.TryParse(_search, out var valBool) ? "=" : string.Empty,

                        TypeCode.Char or TypeCode.String => "contains",

                        TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
                                        or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal
                                        => decimal.TryParse(_search, out var decValue) ? "=" : string.Empty,

                        TypeCode.DateTime => DateTime.TryParse(_search, out var dateValue) ? "=" : string.Empty,

                        _ => string.Empty,
                    };

                    if (@operator == "contains")
                    {
                        where.Add($"{item.PropertyName} != null && {item.PropertyName}.ToLower().Contains(@0.ToLower())");
                    }
                    else if (@operator == "=")
                    {
                        where.Add($"{item.PropertyName} != null && {item.PropertyName} == @0");
                    }
                }
            }

            query = query.Where(where.JoinAsString(" || "), _search);
        }
        else
        {
            //row filter
            foreach (var item in state.FilterDefinitions) { query = query.Where(((FilterDefinition<T>)item).GenerateFilterExpression()); }
        }

        var count = query.Count();

        //order
        if (state.SortDefinitions.Any())
        {
            var orderBy = state.SortDefinitions.OrderBy(a => a.Index)
                                               .Select(a => $"@{a.SortBy} {(a.Descending ? "desc" : "asc")}")
                                               .JoinAsString(", ");
            query = query.OrderBy(orderBy);
        }

        //pagination
        query = query.Skip(state.Page * state.PageSize)
                     .Take(state.PageSize);

        return new()
        {
            Items = query,
            TotalItems = count,
        };
    }

    public PermissionsRead? Permissions { get; set; }
    public HashSet<T> SelectedItems => DataGrid!.SelectedItems;
    public T SelectedItem => DataGrid!.SelectedItem;
}