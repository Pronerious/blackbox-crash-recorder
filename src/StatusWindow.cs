using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlackBox
{
    // ---------------------------------------------------------------- Finestra "Controllo dello stato" (anche benvenuto del primo avvio)
    // Elenco di cosa va bene e cosa no; accanto a ogni problema che si puo' sistemare c'e' un pulsante che lo fa.
    public class StatusWindow
    {
        static readonly Brush Muted = F("#8A91A0"), Text = F("#E6E8EE"), Red = F("#FF5C6C"), Orange = F("#FFB547"), Green = F("#5EE08A"), Blue = F("#4FC3F7"), Back = F("#0E1015");
        static Brush F(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

        readonly Window win, owner;
        readonly Collector col;
        readonly StackPanel body;
        readonly TextBlock result;
        readonly bool welcome;
        readonly Action<string> externalFix;   // correzioni gestite dalla finestra principale (PresentMon, permesso FPS)

        public StatusWindow(Window owner, Collector col, bool welcome, Action<string> externalFix)
        {
            this.owner = owner; this.col = col; this.welcome = welcome; this.externalFix = externalFix;
            win = new Window { Title = welcome ? "BlackBox — benvenuto" : "BlackBox — controllo dello stato", Width = 820, Height = 680, MinWidth = 640, MinHeight = 420,
                               Background = Back, Foreground = Text, FontFamily = new FontFamily("Segoe UI"), Owner = owner, Icon = owner.Icon,
                               WindowStartupLocation = WindowStartupLocation.CenterOwner };
            win.Resources.MergedDictionaries.Add(owner.Resources);
            var dock = new DockPanel { Margin = new Thickness(16) };
            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var close = new Button { Content = welcome ? "Ho capito, inizia" : "Chiudi", MinWidth = 120 };
            close.Click += (s, e) => win.Close();
            var again = new Button { Content = "Ricontrolla" };
            again.Click += (s, e) => Fill();
            DockPanel.SetDock(close, Dock.Right); DockPanel.SetDock(again, Dock.Right);
            bottom.Children.Add(close); bottom.Children.Add(again);
            result = new TextBlock { Foreground = Blue, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            bottom.Children.Add(result);
            DockPanel.SetDock(bottom, Dock.Bottom);
            dock.Children.Add(bottom);
            body = new StackPanel();
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });
            win.Content = dock;
            Fill();
        }

        public void ShowDialog() { win.ShowDialog(); }

        void Title(string t) { body.Children.Add(new TextBlock { Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Muted, Margin = new Thickness(0, 14, 0, 6) }); }

        void Fill()
        {
            body.Children.Clear();
            var health = Health.Check(col);
            var soft = Hardware.Software();
            int lv; string sum = Health.Summary(health, out lv);
            if (welcome)
            {
                body.Children.Add(new TextBlock { Text = "Benvenuto in BlackBox!", FontSize = 22, FontWeight = FontWeights.SemiBold });
                body.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(0, 4, 0, 0),
                    Text = "Ho riconosciuto il tuo PC e scelto le soglie giuste per i tuoi componenti. Qui sotto trovi cosa va già bene e cosa conviene sistemare: dove vedi un pulsante, lo faccio io." });
            }
            else
            {
                body.Children.Add(new TextBlock { Text = lv >= 2 ? "C'è un problema: " + sum : lv == 1 ? "Attenzione: " + sum : "✔  Tutto ok", FontSize = 20, FontWeight = FontWeights.SemiBold,
                                                  Foreground = lv >= 2 ? Red : lv == 1 ? Orange : Green, TextWrapping = TextWrapping.Wrap });
            }

            Title("IL TUO PC");
            body.Children.Add(new TextBlock { Text = Hardware.Describe(), FontFamily = new FontFamily("Consolas"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap });

            Title("PROGRAMMI E IMPOSTAZIONI");
            foreach (var it in soft.OrderByDescending(x => x.Level)) Row(it);

            Title(welcome ? "STATO IN QUESTO MOMENTO" : "CONTROLLO DELLO STATO");
            foreach (var it in health.OrderByDescending(x => x.Level)) Row(it);
            body.Children.Add(new TextBlock { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
                Text = "✔ va bene · ⚠ conviene sistemare · ✖ problema · ℹ solo un'informazione. Massimi da quando BlackBox è acceso: GPU " +
                       N(col.SessionMaxPower) + " W, hotspot " + N(col.SessionMaxHot) + " °C." });
        }

        static string N(double v) { return double.IsNaN(v) ? "--" : Math.Round(v).ToString(); }

        void Row(HealthItem it)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Text = it.Level >= 2 ? "✖" : it.Level == 1 ? "⚠" : it.Level == 0 ? "✔" : "ℹ", FontSize = 14,
                                       Foreground = it.Level >= 2 ? Red : it.Level == 1 ? Orange : it.Level == 0 ? Green : Blue };
            var txt = new TextBlock { Text = it.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(txt, 1);
            g.Children.Add(icon); g.Children.Add(txt);
            if (it.Fix != "")
            {
                var b = new Button { Content = it.FixLabel == "" ? "Sistema" : it.FixLabel, VerticalAlignment = VerticalAlignment.Center };
                string fix = it.Fix;
                b.Click += (s, e) => DoFix(fix);
                Grid.SetColumn(b, 2);
                g.Children.Add(b);
            }
            body.Children.Add(g);
        }

        void DoFix(string fix)
        {
            string msg;
            if (fix == "presentmon" || fix == "fpsperm") { externalFix(fix); msg = ""; }
            else
            {
                try { msg = Hardware.ApplyFix(fix, win); }
                catch (Exception ex) { msg = "Non riuscito: " + ex.Message; }
            }
            if (!string.IsNullOrEmpty(msg)) { result.Text = msg; col.AddEvent("INFO", "Controllo stato: " + msg); }
            Fill();
        }
    }
}
