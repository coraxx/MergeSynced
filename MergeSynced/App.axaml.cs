using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MergeSynced.ViewModels;

namespace MergeSynced
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new Views.MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void AboutMenuItem_OnClick(object? sender, EventArgs e)
        {
            return;
        }
    }
}