using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using UNBEATABLESongImporter.ViewModels;

namespace UNBEATABLESongImporter.Views;

public partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel;
    
    public MainWindow()
    {
        InitializeComponent();
        
        DragDrop.SetAllowDrop(SongDropZone, true);
        SongDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        SongDropZone.AddHandler(DragDrop.DropEvent, OnDrop);
    }
    
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null && files.All(f => f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        
        e.Handled = true;
    }
    
    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        var files = e.Data.GetFiles();
        if (files == null)
        {
            return;
        }
        
        var zipFiles = files
                       .Where(f => f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                       .ToList();

        foreach (var zipFile in zipFiles)
        {
            ViewModel.ImportSong(zipFile.Path.AbsolutePath);
        }
    }
    
    public void OnDirectoryPathTextBoxChange(object? sender, TextChangedEventArgs e)
    {
        ViewModel.OnDirectoryPathTextBoxChange();
    }
}