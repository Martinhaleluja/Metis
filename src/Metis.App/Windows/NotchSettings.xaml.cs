using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Metis.App.Runtime;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Core.Services;

using UserControl = System.Windows.Controls.UserControl;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Metis.App.Windows;

/// <summary>
/// Settings, inside the notch.
///
/// Metis used to keep these in a 980x700 window with an eleven-page sidebar. The
/// window was fine to use and impossible to find: it opened from a tray menu and
/// from a chip most people never noticed, and on first run it was thrown at
/// somebody who had been using Metis for ninety seconds. The notch is the one
/// place people already know where to look, so this is where settings go.
///
/// Two levels rather than a dozen flat pages. The menu is the front door and
/// each section pushes onto it, so Back always returns somewhere recognisable
/// instead of walking whatever the user happened to glance at on the way.
///
/// Not everything has moved yet. The provider and voice pages are two dozen
/// controls each and want their own design pass; until then the menu has a row
/// that opens the old window for them. That is deliberate: a half-ported page
/// that loses a control is worse than an honest link to the page that still has
/// it.
/// </summary>
public partial class NotchSettings : UserControl
{
    private MetisRuntime? _runtime;
    private Action? _openFullSettings;
    private Action? _openSignIn;
    private string _section = string.Empty;
    private bool _loading;

    /// <summary>Where the plan is managed. Anything that costs money happens on
    /// the web, which is where a payment page belongs.</summary>
    // One place, in Metis.Core. This used to be a private copy in each
    // window pointing at a domain that answers 404.
    private static string AccountPageUrl => MetisBackend.AccountPageUrl;

    public NotchSettings()
    {
        InitializeComponent();
        ApplyMenuDensity();
    }

    /// <summary>Raised when the user closes settings.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised whenever the panel's height changes, so the notch can fit it.</summary>
    public event EventHandler? ContentSizeChanged;

    /// <summary>
    /// The menu. Order is by how often somebody needs it, not by how the code is
    /// organised — account first because it is the thing people come looking for.
    /// </summary>
    private sealed record SectionRow(string Key, string Title, string Summary)
    {
        /// <summary>
        /// Whether the explaining line under the title is drawn. Set per
        /// display rather than per section: on a short screen every row loses
        /// it, so the list still reads as one list.
        /// </summary>
        public Visibility SummaryVisibility { get; init; } = Visibility.Visible;

        /// <summary>
        /// Overridden for the screen reader, not for the debugger. An
        /// ItemsControl hands its data item to UI Automation when the container
        /// has no name of its own, so the default record ToString was being
        /// announced verbatim: "SectionRow { Key = Account, Title = Account and
        /// plan, Summary = Who you are signed in as, and what that includes. }".
        /// The row also carries AutomationProperties.Name; this is the backstop
        /// for anywhere the item leaks through regardless.
        /// </summary>
        public override string ToString() => Title;
    }

    private static readonly SectionRow[] Sections =
    [
        new("Account", "Account & Subscription", "Your profile, current tier, usage meters, and plan switcher."),
        new("Intelligence", "AI Provider & Models", "AI reasoning engine, custom models, and personal API keys."),
        new("Voice", "Voice & Speech", "Spoken answers, Gemini natural voices, and speech input."),
        new("General", "Appearance & System", "Your name, theme, sounds, and motion."),
        new("Companion", "Companion Character", "The character that appears when Metis is explaining."),
        new("Privacy", "Screen & Privacy", "What Metis sees, blacklisted apps, and memory."),
        new("Agents", "Background Helpers", "Helper autonomy, execution limits, and notifications."),
        new("Skills", "Skills & Knowledge", "Custom skill notes folder and integrations."),
        new("Diagnostics", "System Diagnostics", "Live engine diagnostics and system logs."),
        new("Updates", "Software Updates", "Current version and check for updates.")
    ];

    /// <summary>
    /// Rebuilds the menu at whatever density this screen has room for.
    ///
    /// Re-run on every open rather than once, because a laptop that gets docked
    /// to a monitor should get its explanations back without a restart — the
    /// same reason MaxBodyHeight reads the work area fresh.
    /// </summary>
    private void ApplyMenuDensity()
    {
        // Measured once from the real style: 12,7 padding, a 12.5pt title and a
        // 10.5pt summary. Approximations rather than a layout pass, because this
        // runs before the list exists and only has to be right to a few pixels.
        const double TallRow = 57;
        const double ShortRow = 36;
        const double MenuChrome = 96;

        var compact = NotchGeometry.ListWantsCompactRows(
            Sections.Length, TallRow, ShortRow, MenuChrome,
            SystemParameters.WorkArea.Height);

        MenuList.ItemsSource = compact
            ? Sections.Select(row => row with { SummaryVisibility = Visibility.Collapsed }).ToArray()
            : Sections;
    }

    public void Attach(MetisRuntime runtime, Action openFullSettings, Action openSignIn)
    {
        _runtime = runtime;
        _openFullSettings = openFullSettings;
        _openSignIn = openSignIn;

        runtime.AccountChanged += (_, _) => Dispatcher.Invoke(RefreshAccount);
        runtime.EntitlementsChanged += (_, _) => Dispatcher.Invoke(RefreshAccount);
    }

    public double MeasureDesiredHeight(double width)
    {
        InvalidateMeasure();
        Measure(new System.Windows.Size(Math.Max(width, 1), double.PositiveInfinity));
        return DesiredSize.Height;
    }

    public void ShowSection(string? section)
    {
        _section = section ?? string.Empty;

        if (_section.Length == 0)
        {
            ApplyMenuDensity();
        }

        MenuPage.Visibility = Collapse(_section.Length == 0);
        AccountPage.Visibility = Collapse(_section == "Account");
        IntelligencePage.Visibility = Collapse(_section == "Intelligence");
        VoicePage.Visibility = Collapse(_section == "Voice");
        GeneralPage.Visibility = Collapse(_section == "General");
        CompanionPage.Visibility = Collapse(_section == "Companion");
        PrivacyPage.Visibility = Collapse(_section == "Privacy");
        AgentsPage.Visibility = Collapse(_section == "Agents");
        SkillsPage.Visibility = Collapse(_section == "Skills");
        UpdatesPage.Visibility = Collapse(_section == "Updates");
        DiagnosticsPage.Visibility = Collapse(_section == "Diagnostics");

        BackButton.Visibility = Collapse(_section.Length > 0);
        Breadcrumb.Visibility = Collapse(_section.Length > 0);

        SaveRow.Visibility = Collapse(_section is "General" or "Companion" or "Privacy" or "Agents" or "Intelligence" or "Voice" or "Skills");

        PageTitle.Text = _section.Length == 0
            ? "Settings"
            : Sections.FirstOrDefault(row => row.Key == _section)?.Title ?? "Settings";

        if (_section == "Account")
        {
            RefreshAccount();
        }
        else if (_section == "Intelligence")
        {
            RefreshIntelligence();
        }
        else if (_section == "Voice")
        {
            RefreshVoice();
        }
        else if (_section == "General")
        {
            RefreshGeneral();
        }
        else if (_section == "Companion")
        {
            RefreshCompanion();
        }
        else if (_section == "Privacy")
        {
            RefreshPrivacy();
        }
        else if (_section == "Agents")
        {
            RefreshAgents();
        }
        else if (_section == "Skills")
        {
            RefreshSkills();
        }
        else if (_section == "Updates")
        {
            RefreshUpdates();
        }
        else if (_section == "Diagnostics")
        {
            RefreshDiagnostics();
        }

        // Laid out before anybody measures it. The refresh above has just
        // changed which page is visible and filled its lists, and a measure
        // taken before that has been arranged returns the height of the page
        // that was showing a moment ago — which is how the notch ended up
        // sized for the section you had just left.
        UpdateLayout();
        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Visibility Collapse(bool visible) =>
        visible ? Visibility.Visible : Visibility.Collapsed;

    private void Back_OnClick(object sender, RoutedEventArgs e)
    {
        SaveStatus.Text = string.Empty;
        ShowSection(string.Empty);
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        SaveStatus.Text = string.Empty;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MenuRow_OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && sender is FrameworkElement element)
        {
            e.Handled = true;
            ActivateSection(element.Tag as string);
        }
    }

    private void MenuRow_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement element)
        {
            ActivateSection(element.Tag as string);
        }
    }

    private void ActivateSection(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        ShowSection(key);
    }

    // ============================== Account ==============================

    public sealed record FeatureItem(string StatusSymbol, System.Windows.Media.Brush StatusBrush, string Label)
    {
        public override string ToString() => $"{Label}: {(StatusSymbol == "✓" ? "Included" : "Not included")}";
    }

    /// <summary>
    /// One plan, as the switcher draws it. Priced and described here rather
    /// than in the markup so the app and the website read the same numbers from
    /// PlanCatalogue.
    /// </summary>
    private sealed record PlanRowItem(
        PlanTier Key,
        string Name,
        string Price,
        string Cadence,
        string Summary,
        Brush EdgeBrush,
        Visibility CurrentVisibility,
        string AutomationName)
    {
        public override string ToString() => AutomationName;
    }

    /// <summary>
    /// One allowance and how much of it is gone.
    ///
    /// The two GridLengths are the bar. Star widths rather than a pixel Width
    /// mean the meter is right at any notch size and after any resize, with no
    /// layout code and nothing to recompute when the panel is measured again —
    /// the previous version set Width from ActualWidth during a refresh, which
    /// is zero the first time the page is built and stayed zero.
    /// </summary>
    private sealed record UsageRow(
        string Label, string Detail, GridLength Used, GridLength Left, Brush FillBrush)
    {
        public override string ToString() => $"{Label}: {Detail}";
    }

    private void RefreshAccount()
    {
        if (_runtime is null)
        {
            return;
        }

        var account = _runtime.Account;
        var plan = account.Plan;

        // Not overwritten while it is being typed into, or the refresh that
        // follows any account event would yank the caret back to the stored
        // value mid-word.
        if (!AccountNameBox.IsKeyboardFocused)
        {
            AccountNameBox.Text = string.IsNullOrWhiteSpace(account.DisplayName)
                ? _runtime.Settings.UserName
                : account.DisplayName;
        }
        // Three states, not two. An account that is signed in but has no email
        // on file used to read "Not signed in", directly above a Sign out
        // button — which tells the user their session is gone when it is not.
        AccountEmail.Text = account.IsSignedIn
            ? string.IsNullOrWhiteSpace(account.Email)
                ? "Signed in"
                : account.Email
            : "Not signed in";
        AvatarEmoji.Text = string.IsNullOrWhiteSpace(account.Avatar) ? "\U0001F98A" : account.Avatar;

        VerifiedBadge.Visibility = Collapse(account.IsSignedIn && account.EmailVerified);
        SignInChip.Visibility = Collapse(!account.IsSignedIn);
        SignOutChip.Visibility = Collapse(account.IsSignedIn);
        ManageWebChip.Visibility = Collapse(account.IsSignedIn);

        // What a row offers depends on who is reading it. Staff switch in place,
        // because that is how a plan gate is watched working; everyone else is
        // taken to the website, because a plan is bought rather than chosen. The
        // announced name has to say which, or a screen reader promises a switch
        // that opens a browser instead.
        var switchesInPlace = _runtime.CanSwitchPlanLocally;

        PlanList.ItemsSource = PlanCatalogue.All
            .Select(entry => new PlanRowItem(
                entry.Tier,
                entry.Name,
                entry.PriceLabel,
                entry.Cadence,
                entry.Summary,
                Themed(entry.Tier == plan ? "AccentBrush" : "NotchHairlineBrush", 0x8E, 0x8E, 0x93),
                Collapse(entry.Tier == plan),
                entry.Tier == plan
                    ? $"{entry.Name}, {entry.PriceLabel} {entry.Cadence}. Your plan. {entry.Summary}"
                    : switchesInPlace
                        ? $"Switch to {entry.Name}, {entry.PriceLabel} {entry.Cadence}. {entry.Summary}"
                        : $"{entry.Name}, {entry.PriceLabel} {entry.Cadence}. {entry.Summary} Opens the website to change plan."))
            .ToArray();

        PlanSwitcherNote.Text = !account.IsSignedIn
            ? "Sign in to choose a plan. Metis works signed out on a model running on your own computer."
            : switchesInPlace
                ? "Staff account: choosing a plan here switches to it immediately, so a plan's limits can be seen from the inside."
                : "Your plan is changed on the website, and Metis picks the change up within a few minutes. Everything below is what each plan includes.";

        RefreshUsage(plan);

        FeatureList.ItemsSource = DescribeIncluded();
        FeatureNote.Text = string.Empty;

        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The three meters, in the units the plan is sold in.
    ///
    /// A single bar against a dollar budget was the one number a customer
    /// cannot act on: nobody knows whether forty cents of Gemini is a lot, or
    /// how many more questions it buys. Talk messages, minutes and agent
    /// messages are countable by the person spending them, and they are the
    /// words on the pricing page.
    /// </summary>
    private void RefreshUsage(PlanTier plan)
    {
        var limits = _runtime?.Entitlements?.Limits ?? PlanCatalogue.LimitsFor(plan);
        var allowance = _runtime?.LastAllowance;

        var rows = new List<UsageRow>
        {
            Meter("Talk messages", allowance?.TurnsUsed ?? 0, limits.MaxTurnsPerMonth, "a month"),
            Meter("Dictation", allowance?.DictationMinutesUsed ?? 0,
                limits.MaxDictationMinutesPerMonth, "a month", unit: " min"),
            Meter("Agent messages", allowance?.AgentStepsUsed ?? 0,
                limits.MaxAgentStepsPerMonth, "a month")
        };

        UsageList.ItemsSource = rows;
        UsageCard.Visibility = Visibility.Visible;

        UsageDetail.Text = allowance is null
            ? "Metis has not been able to reach its account service, so these are the allowances your plan includes rather than what you have used."
            : $"Resets {allowance.ResetsUtc.ToLocalTime():d MMMM}. Answers on a model running on your own computer are never counted.";
    }

    /// <summary>
    /// One meter. A cap of zero means the plan does not count this at all,
    /// which is drawn as a full quiet bar rather than an empty one: an empty
    /// bar reads as "none included", which is the opposite of what it means.
    /// </summary>
    private UsageRow Meter(string label, int used, int cap, string period, string unit = "")
    {
        if (cap <= 0)
        {
            return new UsageRow(
                label,
                "Unlimited",
                new GridLength(1, GridUnitType.Star),
                new GridLength(0, GridUnitType.Star),
                Themed("NotchScrollThumbBrush", 0x8E, 0x8E, 0x93));
        }

        var share = Math.Clamp(used / (double)cap, 0, 1);

        // Amber past three quarters, red once it is gone. The colour is the only
        // warning somebody gets before a refusal, so it has to arrive before the
        // bar is full rather than at the moment it is too late.
        var brush = share >= 1
            ? Themed("NotchDangerInkBrush", 0xFF, 0x62, 0x57)
            : share >= 0.75
                ? Themed("NotchWarningInkBrush", 0xFF, 0x9F, 0x0A)
                : Themed("AccentBrush", 0x0A, 0x7C, 0xFF);

        return new UsageRow(
            label,

            // Grouped, and in the reader's own locale. Two thousand agent
            // messages rendered as "2000" is a number people have to count the
            // digits of; the pricing page writes it as 2,000 and so should this.
            $"{used:N0}{unit} of {cap:N0}{unit} {period}",
            new GridLength(share, GridUnitType.Star),
            new GridLength(1 - share, GridUnitType.Star),
            brush);
    }

    private IReadOnlyList<FeatureItem> DescribeIncluded()
    {
        // Named after what they do rather than after a plan, and the plan that
        // includes them comes from Entitlements. The labels used to carry the
        // tier in brackets — "(Plus & Pro)", "(Pro)" — which meant renaming a
        // plan silently made this list lie, and it did: it was still offering
        // Plus months after Plus stopped existing.
        (MetisFeature Feature, string Label)[] features =
        [
            (MetisFeature.ManagedScreenVision, "Reading your screen when you ask"),
            (MetisFeature.AutonomousAgents, "Background agents that get on with a job"),
            (MetisFeature.PersistentMemory, "Remembering what you are working on"),
            (MetisFeature.ManagedPremiumModels, "The larger AI models"),
            (MetisFeature.AdvancedAutomation, "Advanced automation and region inspect"),
            (MetisFeature.BrowserAssistance, "Help with what is in your browser"),
            (MetisFeature.CustomAiProvider, "Answering on your own AI account"),
            (MetisFeature.AdvancedAgents, "Agents that hand work to each other"),
            (MetisFeature.SystemCommands, "Running background system tools"),
        ];

        var greenBrush = Fill("#30B158");
        var mutedBrush = Fill("#8E8E93");

        return features.Select(item =>
        {
            var allowed = _runtime?.Can(item.Feature) ?? false;
            return new FeatureItem(
                allowed ? "✓" : "—",
                allowed ? greenBrush : mutedBrush,
                item.Label
            );
        }).ToArray();
    }

    /// <summary>
    /// Commits the name on Enter, and abandons the edit on Escape.
    /// </summary>
    private void AccountName_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Keyboard.ClearFocus();
            _ = SaveDisplayNameAsync();
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RefreshAccount();
            Keyboard.ClearFocus();
        }
    }

    private void AccountName_OnLostFocus(object sender, RoutedEventArgs e) =>
        _ = SaveDisplayNameAsync();

    /// <summary>
    /// Saves what Metis calls you, in both the places that answer that question.
    ///
    /// AppSettings.UserName is what the assistant uses when it talks to you and
    /// works signed out; the account's display name is what the header shows and
    /// what the website will see. They were allowed to drift, so the greeting
    /// and the profile could disagree about your name.
    /// </summary>
    private async System.Threading.Tasks.Task SaveDisplayNameAsync()
    {
        if (_runtime is null || _loading)
        {
            return;
        }

        var name = AccountNameBox.Text.Trim();
        if (name.Length == 0 || name == _runtime.Account.DisplayName)
        {
            return;
        }

        try
        {
            await _runtime.UpdateProfileAsync(name, _runtime.Account.Avatar);
            await _runtime.SaveSettingsAsync(
                _runtime.Settings with { UserName = name },
                newGeminiApiKey: null,
                newOpenAiApiKey: null);

            SaveStatus.Text = $"Metis will call you {name}.";
        }
        catch (Exception exception)
        {
            _runtime.Log.Error("The display name could not be saved.", exception);
            SaveStatus.Text = "That name could not be saved.";
        }
    }

    private void Avatar_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AvatarPickerTray.Visibility = AvatarPickerTray.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void PickAvatar_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string avatarTag } || _runtime is null)
        {
            return;
        }

        e.Handled = true;
        AvatarEmoji.Text = avatarTag;
        AvatarPickerTray.Visibility = Visibility.Collapsed;
        await _runtime.UpdateProfileAsync(_runtime.Account.DisplayName, avatarTag);
        RefreshAccount();
    }

    /// <summary>
    /// Changes plan. One handler rather than three near-identical ones, so a
    /// plan added to the catalogue needs no code here at all.
    ///
    /// What a click does depends on who is clicking, and that is the whole
    /// point. For staff it switches in place, which is how a plan gate gets
    /// watched working from both sides without buying anything. For everybody
    /// else it opens the website, because a plan is a purchase — and a desktop
    /// application that could hand out the paid tier for a click was not a
    /// testing convenience, it was the paywall.
    /// </summary>
    private async void SelectPlan_OnClick(object sender, RoutedEventArgs e)
    {
        if (_runtime is null || sender is not Button { Tag: PlanTier tier })
        {
            return;
        }

        if (tier == _runtime.Account.Plan)
        {
            return;
        }

        if (await ChangePlanAsync(tier))
        {
            RefreshAccount();
        }
    }

    /// <summary>
    /// Moves the account on to a plan, or sends the user where they can buy it.
    ///
    /// True when the plan actually changed here, so the caller knows whether it
    /// has anything to redraw. False covers both "not allowed to" and "the
    /// browser has it now", and in neither case has anything on this panel
    /// moved.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> ChangePlanAsync(PlanTier tier)
    {
        if (_runtime is null)
        {
            return false;
        }

        if (!_runtime.CanSwitchPlanLocally)
        {
            OpenAccountPage();
            return false;
        }

        await _runtime.SetPlanAsync(tier);
        return true;
    }

    private void ManagePlan_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenAccountPage();
    }

    /// <summary>
    /// Opens the account page, and asks the gateway again a little afterwards.
    ///
    /// Buying happens on the website and the desktop has no way of being told
    /// when it finishes, so it has to ask. Forty seconds is long enough to have
    /// paid for something and short enough that the notch is still the thing the
    /// user was looking at; it is a guess, and it is allowed to be one because
    /// nothing depends on it — the runtime asks again every fifteen minutes, and
    /// again whenever this page is opened.
    /// </summary>
    private void OpenAccountPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AccountPageUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _runtime?.Log.Error("Could not open the account page in a browser.", exception);
            return;
        }

        _ = RefreshAfterWebVisitAsync();
    }

    private async System.Threading.Tasks.Task RefreshAfterWebVisitAsync()
    {
        if (_runtime is null)
        {
            return;
        }

        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(40));

        // Back on the interface thread: this began on it, so the continuation
        // returns to it and RefreshAccount may touch the panel directly.
        await _runtime.RefreshEntitlementsAsync();
        RefreshAccount();
    }

    private void SignIn_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _openSignIn?.Invoke();
    }

    private void SignOut_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _runtime?.SignOut();
        RefreshAccount();
    }

    // =========================== Intelligence ===========================

    private void RefreshIntelligence()
    {
        if (_runtime is null) return;

        _loading = true;
        try
        {
            var s = _runtime.Settings;
            SelectCombo(AiProviderBox, s.AiProvider);

            GeminiModelBox.Text = s.ReasoningModel;
            OpenAiModelBox.Text = s.OpenAiReasoningModel;
            ClaudeModelBox.Text = s.ClaudeReasoningModel;
            OpenRouterModelBox.Text = s.OpenRouterModel;
            OllamaEndpointBox.Text = s.OllamaEndpoint;
            OllamaModelBox.Text = s.OllamaModel;

            var plan = _runtime.Account.Plan;
            var offer = PlanCatalogue.For(plan);
            MetisTierModelSummary.Text = $"{offer.Name} \u2014 {offer.Summary}";

            UpdateIntelligencePanels();
        }
        finally
        {
            _loading = false;
        }
    }

    private void UpdateIntelligencePanels()
    {
        if (_runtime is null) return;

        var provider = SelectedText(AiProviderBox, "Metis");
        var canBringOwn = _runtime.Can(MetisFeature.CustomAiProvider);

        MetisManagedCard.Visibility = Collapse(provider is "Metis" or "Automatic");

        var isByoProvider = provider is "Gemini" or "OpenAI" or "Claude" or "OpenRouter";
        ByoGatingCard.Visibility = Collapse(isByoProvider && !canBringOwn);

        // Which plan this belongs to is asked rather than assumed, so renaming
        // the top plan does not leave the banner advertising the wrong one. The
        // question used to be asked with these four lines in place; it is one
        // call now because three other panels needed the same answer and were
        // each writing the name out by hand instead.
        var byoPlan = OwnKeyPlan;

        ByoGatingLabel.Text = $"\U0001F512 PART OF METIS {byoPlan.Name.ToUpperInvariant()}";
        ByoGatingBody.Text =
            $"Answering on your own OpenAI, Anthropic, Gemini or OpenRouter account is part of "
            + $"Metis {byoPlan.Name}, {byoPlan.PriceLabel} a month. Your provider bills you for what "
            + "the models cost, separately from that.";
        ByoUpgradeLabel.Text = _runtime.CanSwitchPlanLocally
            ? $"Switch to {byoPlan.Name} ({byoPlan.PriceLabel}/mo)"
            : $"Get {byoPlan.Name} ({byoPlan.PriceLabel}/mo) ↗";

        GeminiCard.Visibility = Collapse(provider == "Gemini" && canBringOwn);
        OpenAiCard.Visibility = Collapse(provider == "OpenAI" && canBringOwn);
        ClaudeCard.Visibility = Collapse(provider == "Claude" && canBringOwn);
        OpenRouterCard.Visibility = Collapse(provider == "OpenRouter" && canBringOwn);
        OllamaCard.Visibility = Collapse(provider == "Ollama");

        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows only the fields the chosen speech-to-text route actually uses.
    /// </summary>
    private void UpdateDictationPanels()
    {
        var provider = SelectedText(SpeechToTextProviderBox, "Native");
        NativeSttPanel.Visibility = provider == "Native" ? Visibility.Visible : Visibility.Collapsed;
        AssemblyAiPanel.Visibility = provider == "AssemblyAI" ? Visibility.Visible : Visibility.Collapsed;
        WhisperPanel.Visibility = provider == "Whisper.cpp" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SpeechToTextProviderBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        UpdateDictationPanels();
    }

    /// <summary>
    /// Records a few seconds and shows what came back, so "is dictation
    /// working?" has an answer on screen rather than being something the user
    /// has to infer from a question that went nowhere.
    /// </summary>
    private async void TestDictation_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_runtime is null) return;

        try
        {
            DictationStatus.Text = "Listening for three seconds — say something…";
            var heard = await _runtime.TestDictationAsync();
            DictationStatus.Text = string.IsNullOrWhiteSpace(heard)
                ? "Nothing was heard. Check the microphone above and try again."
                : $"Heard: “{heard}”";
        }
        catch (Exception exception)
        {
            DictationStatus.Text = exception.Message;
        }
    }

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private void AiProviderBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        UpdateIntelligencePanels();
    }

    private async void TestProvider_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_runtime is null) return;

        try
        {
            AiProviderStatus.Text = "Testing connection…";
            var provider = SelectedText(AiProviderBox, "Metis");
            var result = provider switch
            {
                "OpenAI" => await _runtime.TestOpenAiModelAsync(_runtime.Settings.OpenAiReasoningModel),
                "Gemini" or "Automatic" or "Metis" => await _runtime.TestModelAsync(_runtime.Settings.ReasoningModel),
                "Claude" => await _runtime.TestReasoningProviderAsync(provider, _runtime.Settings.ClaudeReasoningModel),
                "OpenRouter" => await _runtime.TestReasoningProviderAsync(provider, _runtime.Settings.OpenRouterModel),
                _ => await _runtime.TestReasoningProviderAsync(provider, _runtime.Settings.OllamaModel)
            };

            AiProviderStatus.Text = result.Message;
        }
        catch (Exception ex)
        {
            AiProviderStatus.Text = ex.Message;
        }
    }

    /// <summary>
    /// The plan that includes answering on a key of your own.
    ///
    /// Asked of the entitlement rules rather than named, because the panels that
    /// mention it are the ones that got it wrong: the banner offered "Switch to
    /// Max" and the button underneath it set the account to Pro, which cannot
    /// bring its own key at all. One source, so the label and the action cannot
    /// disagree again.
    /// </summary>
    private static PlanOffer OwnKeyPlan =>
        PlanCatalogue.SmallestPlanWith(MetisFeature.CustomAiProvider) ?? PlanCatalogue.Max;

    /// <summary>
    /// Takes the account to the plan that allows a key of its own — or to the
    /// page where it can be bought.
    /// </summary>
    private async void UpgradeForOwnKey_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (await ChangePlanAsync(OwnKeyPlan.Tier))
        {
            RefreshAccount();
            RefreshIntelligence();
        }
    }

    /// <summary>
    /// Moves up one plan, whatever the current one is.
    ///
    /// It used to say "upgrade to Plus" unconditionally, which offered the
    /// middle plan to somebody already on it. PlanCatalogue.NextAfter knows
    /// where the ladder ends, so the prompt is simply not shown at the top.
    /// </summary>
    private async void UpgradePlan_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (_runtime is null ||
            PlanCatalogue.NextAfter(_runtime.Account.Plan) is not { } next)
        {
            return;
        }

        if (await ChangePlanAsync(next.Tier))
        {
            RefreshAccount();
            RefreshAgents();
        }
    }

    private void RemoveKey_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string provider } || _runtime is null) return;
        e.Handled = true;

        var confirm = MessageBox.Show(
            $"Remove the stored {provider} API key?",
            "Metis",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK) return;

        try
        {
            _runtime.DeleteProviderKey(provider);
            AiProviderStatus.Text = $"{provider} key removed.";
        }
        catch (Exception ex)
        {
            AiProviderStatus.Text = ex.Message;
        }
    }

    // ============================== Voice ==============================

    private void RefreshVoice()
    {
        if (_runtime is null) return;

        _loading = true;
        try
        {
            var s = _runtime.Settings;
            SpeechEnabledCheck.IsChecked = s.SpeechEnabled;
            SpeakErrorsCheck.IsChecked = s.SpeakErrorsAloud;
            SelectCombo(GeminiVoiceBox, s.VoiceName);

            SelectCombo(SpeechToTextProviderBox, s.SpeechToTextProvider);
            AssemblyAiModelBox.Text = s.AssemblyAiModel;
            WhisperCppExecutablePathBox.Text = s.WhisperCppExecutablePath;
            WhisperCppModelPathBox.Text = s.WhisperCppModelPath;
            UpdateDictationPanels();
            DictationStatus.Text = string.Empty;

            try
            {
                var devices = _runtime.GetInputDevices();
                MicrophoneBox.ItemsSource = devices;
                MicrophoneBox.DisplayMemberPath = nameof(AudioDeviceInfo.Name);
                MicrophoneBox.SelectedValuePath = nameof(AudioDeviceInfo.Id);
                MicrophoneBox.SelectedValue = s.PreferredMicrophoneId ?? devices.FirstOrDefault()?.Id;
                MicrophoneStatus.Text = devices.Count == 0
                    ? "No microphone found."
                    : $"{devices.Count} audio input device(s) detected.";
            }
            catch (Exception ex)
            {
                MicrophoneStatus.Text = ex.Message;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async void PreviewVoice_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_runtime is null) return;

        try
        {
            VoicePreviewStatus.Text = "Playing voice preview…";
            var voice = SelectedText(GeminiVoiceBox, "Kore");
            var result = await _runtime.PreviewVoiceAsync(voice, _runtime.Settings.SpeechModel);
            VoicePreviewStatus.Text = result.Message;
        }
        catch (Exception ex)
        {
            VoicePreviewStatus.Text = ex.Message;
        }
    }

    // ============================== Skills ==============================

    private void RefreshSkills()
    {
        if (_runtime is null) return;

        _loading = true;
        try
        {
            var s = _runtime.Settings;
            UserSkillsCheck.IsChecked = s.UserSkillsEnabled;
            SkillsFolderBox.Text = s.SkillsFolder;

            // Both halves named a plan they had no business naming. The refusal
            // advertised Pro for a capability that is part of Max, so it sold
            // the wrong subscription; the granted line said "your Pro plan" to
            // everybody who had it, including the Max subscribers who had paid
            // more than that. Both come from the catalogue now.
            var hasSystem = _runtime.Can(MetisFeature.SystemCommands);
            SystemToolsSummary.Text = hasSystem
                ? "✓ System terminal commands and diagnostic actions are unlocked on your "
                  + $"{PlanCatalogue.For(_runtime.Account.Plan).Name} plan."
                : "🔒 System automation commands are part of Metis "
                  + $"{PlanCatalogue.NameOfPlanWith(MetisFeature.SystemCommands)}.";
        }
        finally
        {
            _loading = false;
        }
    }

    private void OpenSkillsFolder_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_runtime is null) return;

        try
        {
            _runtime.ReloadUserSkills();
            var folder = SkillsFolderBox.Text.Trim();
            if (!Path.IsPathRooted(folder))
            {
                folder = Path.Combine(AppContext.BaseDirectory, folder);
            }
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _runtime.Log.Error("Could not open skills folder.", ex);
        }
    }

    // ============================== General ==============================

    private void RefreshGeneral()
    {
        if (_runtime is null) return;

        _loading = true;
        try
        {
            var settings = _runtime.Settings;
            UserNameBox.Text = settings.UserName;
            StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
            ReduceMotionCheck.IsChecked = settings.ReduceMotion;
            SoundsCheck.IsChecked = settings.ActivationSoundsEnabled;

            _theme = settings.ThemePreference;
            HighlightTheme(_theme);

            SaveStatus.Text = string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private string _theme = "System";

    private void HighlightTheme(string? theme)
    {
        _theme = theme switch
        {
            "Light" => "Light",
            "Dark" => "Dark",
            _ => "System"
        };

        foreach (var (chip, tag) in new[]
                 {
                     (ThemeSystemChip, "System"),
                     (ThemeLightChip, "Light"),
                     (ThemeDarkChip, "Dark")
                 })
        {
            chip.BorderBrush = Themed(
                tag == _theme ? "AccentBrush" : "NotchChipEdgeBrush", 0x8E, 0x8E, 0x93);
            chip.BorderThickness = new Thickness(tag == _theme ? 2 : 1);
        }
    }

    private void Theme_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string themeTag }) return;

        e.Handled = true;
        HighlightTheme(themeTag);
    }

    // ============================= Companion =============================

    private string _companionColour = "Sapphire";
    private string _companionCharacter = CompanionShapes.DefaultName;

    /// <summary>
    /// A character as the picker needs it: the geometry already parsed.
    ///
    /// The catalogue stores each form as path-mark-up text because it is shared
    /// with code that has no WPF in it. Parsing here rather than binding the
    /// string straight to <c>Path.Data</c> keeps that conversion somewhere it
    /// can fail loudly, instead of silently rendering nothing.
    /// </summary>
    private sealed record CompanionCharacterItem(
        string Name,
        string Description,
        System.Windows.Media.Geometry Figure)
    {
        /// <summary>
        /// What a screen reader says when it reaches this tile.
        ///
        /// The button's content is a silhouette, so without this the only thing
        /// to read out is the record's compiler-generated field dump -- which
        /// would include the parsed path geometry. The name and the sentence
        /// beneath it are what a sighted user is choosing between, so they are
        /// what gets read.
        /// </summary>
        public override string ToString() => $"{Name}. {Description}";
    }

    private void RefreshCompanion()
    {
        if (_runtime is null) return;

        _loading = true;
        try
        {
            var settings = _runtime.Settings;
            _companionColour = settings.CompanionColor;

            CompanionAlwaysCheck.IsChecked = settings.CompanionAlwaysVisible;
            CompanionSizeSlider.Value = settings.CompanionSize;
            CompanionSizeLabel.Text = SizeWords(settings.CompanionSize);
            CursorDistanceSlider.Value = settings.CursorDistance;
            CursorDistanceLabel.Text = DistanceWords(settings.CursorDistance);
            CompanionColourName.Text = _companionColour;

            CompanionColours.ItemsSource = CompanionPalette.All;

            _companionCharacter = CompanionShapes.Normalize(settings.CompanionShape);
            CompanionCharacters.ItemsSource = CompanionShapes.All
                .Select(shape => new CompanionCharacterItem(
                    shape.Name,
                    shape.Description,
                    System.Windows.Media.Geometry.Parse(shape.Geometry)))
                .ToList();

            // After layout, not now. An ItemsControl generates its containers
            // during the arrange that follows this method, so walking the visual
            // tree here finds no buttons at all and the ring around the chosen
            // colour is simply never drawn — which looks exactly like a picker
            // that does not respond to being clicked.
            Dispatcher.InvokeAsync(HighlightCompanionColour, DispatcherPriority.Loaded);
            Dispatcher.InvokeAsync(HighlightCompanionCharacter, DispatcherPriority.Loaded);

            SaveStatus.Text = string.Empty;
        }
        finally
        {
            _loading = false;
        }

        CompanionSizeSlider.ValueChanged -= CompanionSize_OnChanged;
        CompanionSizeSlider.ValueChanged += CompanionSize_OnChanged;
        CursorDistanceSlider.ValueChanged -= CursorDistance_OnChanged;
        CursorDistanceSlider.ValueChanged += CursorDistance_OnChanged;
    }

    private void CompanionSize_OnChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e) =>
        CompanionSizeLabel.Text = SizeWords((int)e.NewValue);

    private void CursorDistance_OnChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e) =>
        CursorDistanceLabel.Text = DistanceWords((int)e.NewValue);

    /// <summary>
    /// Sizes in words as well as pixels. "56 px" tells nobody how big the thing
    /// on their screen will be until they have already tried it.
    /// </summary>
    private static string SizeWords(int size) => size switch
    {
        <= 40 => $"Small \u2014 {size} px",
        <= 64 => $"Normal \u2014 {size} px",
        <= 88 => $"Large \u2014 {size} px",
        _ => $"Very large \u2014 {size} px"
    };

    private static string DistanceWords(int distance) => distance switch
    {
        0 => "Right on the pointer",
        <= 15 => $"Very close \u2014 {distance} px from the pointer",
        <= 40 => $"Close \u2014 {distance} px from the pointer",
        <= 80 => $"A little away \u2014 {distance} px from the pointer",
        _ => $"Well clear of it \u2014 {distance} px from the pointer"
    };

    private void HighlightCompanionColour()
    {
        foreach (var button in Descendants<Button>(CompanionColours))
        {
            var isSelected = button.Tag as string == _companionColour;
            button.BorderBrush = isSelected
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : (System.Windows.Media.Brush)FindResource("NotchChipEdgeBrush");
            button.BorderThickness = new Thickness(isSelected ? 2.5 : 1.5);
        }

        CompanionColourName.Text = _companionColour;
    }

    private void CompanionColour_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;

        _companionColour = name;
        HighlightCompanionColour();
    }

    private void HighlightCompanionCharacter()
    {
        foreach (var button in Descendants<Button>(CompanionCharacters))
        {
            var isSelected = button.Tag as string == _companionCharacter;
            button.BorderBrush = isSelected
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : (System.Windows.Media.Brush)FindResource("NotchChipEdgeBrush");
            button.BorderThickness = new Thickness(isSelected ? 2.5 : 1.5);
        }

        CompanionCharacterName.Text = CompanionShapes.Resolve(_companionCharacter).Description;
    }

    private void CompanionCharacter_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;

        _companionCharacter = name;
        HighlightCompanionCharacter();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    // =============================== Privacy ===============================

    private void RefreshPrivacy()
    {
        if (_runtime is null) return;

        _loading = true;
        try
        {
            var s = _runtime.Settings;
            CaptureScreenCheck.IsChecked = s.CaptureActiveWindow;
            VisualGuidanceCheck.IsChecked = s.VisualGuidanceEnabled;
            ExcludedAppsBox.Text = s.ExcludedApplications;
            MemoryEnabledCheck.IsChecked = s.MemoryEnabled;
            ChatMemoryCheck.IsChecked = s.ChatMemoryEnabled;
            SaveStatus.Text = string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private async void ClearMemory_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_runtime is null) return;

        var confirm = MessageBox.Show(
            "Erase everything Metis has learned about which steps you already know?",
            "Metis",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK) return;

        try
        {
            await _runtime.ClearMemoryAsync();
            PrivacyStatus.Text = "Memory cleared.";
        }
        catch (Exception ex)
        {
            PrivacyStatus.Text = ex.Message;
        }
    }

    private void ClearHistory_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_runtime is null) return;

        var confirm = MessageBox.Show(
            "Delete every saved conversation? This cannot be undone.",
            "Metis",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK) return;

        try
        {
            _runtime.ClearAllChats();
            PrivacyStatus.Text = "Chat history deleted.";
        }
        catch (Exception ex)
        {
            PrivacyStatus.Text = ex.Message;
        }
    }

    // ================================ Agents ================================

    private string _autonomy = "AskApproval";

    private void RefreshAgents()
    {
        if (_runtime is null) return;

        _loading = true;
        try
        {
            var settings = _runtime.Settings;
            // Agents are on every plan; what differs is how many messages a
            // month, so the panel is always usable and the card above it says
            // what this plan includes.
            var allowed = _runtime.Can(MetisFeature.AutonomousAgents);
            var offer = PlanCatalogue.For(_runtime.Account.Plan);
            var next = PlanCatalogue.NextAfter(_runtime.Account.Plan);

            AgentsConfigPanel.Visibility = Collapse(allowed);
            AgentsAllowanceCard.Visibility = Visibility.Visible;
            AgentsAllowanceLabel.Text =
                $"{offer.Limits.MaxAgentStepsPerMonth} AGENT MESSAGES A MONTH ON {offer.Name.ToUpperInvariant()}";
            AgentsAllowanceBody.Text =
                "An agent gets on with a job while you work. Every step it takes is one message, "
                + "so a long job spends several.";

            AgentsUpgradeChip.Visibility = Collapse(next is not null);
            if (next is not null)
            {
                // "Switch to" only for the accounts that really can. For
                // everyone else the chip opens the website, and a label that
                // promised a switch would be describing something else.
                AgentsUpgradeLabel.Text = _runtime.CanSwitchPlanLocally
                    ? $"{next.Name} includes {next.Limits.MaxAgentStepsPerMonth:N0} \u2014 switch to {next.Name}"
                    : $"{next.Name} includes {next.Limits.MaxAgentStepsPerMonth:N0} \u2014 see {next.Name} \u2197";
            }

            HighlightAutonomy(settings.AgentAutonomyMode);
            AgentTurnsSlider.Value = settings.AgentMaxTurns;
            AgentTurnsLabel.Text = TurnsLabel(settings.AgentMaxTurns);
            AgentNotificationsCheck.IsChecked = settings.AgentWindowsNotificationsEnabled;

            SaveStatus.Text = string.Empty;
        }
        finally
        {
            _loading = false;
        }

        AgentTurnsSlider.ValueChanged -= AgentTurns_OnChanged;
        AgentTurnsSlider.ValueChanged += AgentTurns_OnChanged;
    }

    private void AgentTurns_OnChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e) =>
        AgentTurnsLabel.Text = TurnsLabel((int)e.NewValue);

    private static string TurnsLabel(int turns) =>
        $"Up to {turns} steps before an agent gives up and reports results.";

    private void HighlightAutonomy(string? mode)
    {
        _autonomy = mode == "Autonomous" ? "Autonomous" : "AskApproval";

        foreach (var (chip, key) in new[]
                 {
                     (AskApprovalChip, "AskApproval"),
                     (AutonomousChip, "Autonomous")
                 })
        {
            chip.BorderBrush = Themed(
                key == _autonomy ? "AccentBrush" : "NotchChipEdgeBrush", 0x8E, 0x8E, 0x93);
            chip.BorderThickness = new Thickness(key == _autonomy ? 2 : 1);
        }

        AutonomyNote.Text = _autonomy == "Autonomous"
            ? "Agents execute tool actions directly. Ideal for non-destructive tasks."
            : "Agents ask for approval before critical actions. Recommended for safety.";
    }

    private void Autonomy_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string key }) return;
        e.Handled = true;
        HighlightAutonomy(key);
    }

    // =============================== Updates ===============================

    private void RefreshUpdates()
    {
        VersionLabel.Text = $"Metis v{AppVersion.Current}";
        UpdateStatus.Text = _updateNote;
    }

    private string _updateNote =
        "Metis checks on its own when it starts. Click below to check manually.";

    private async void CheckUpdates_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_runtime is null) return;

        _updateNote = "Checking for updates…";
        UpdateStatus.Text = _updateNote;

        try
        {
            var check = await new UpdateService(_runtime.Log).CheckAsync();
            _updateNote = check.UpdateAvailable
                ? $"Metis {check.Version} is available. Open the notch header banner to install."
                : "You are running the latest version of Metis.";
        }
        catch (Exception exception)
        {
            _runtime.Log.Error("Checking for update failed.", exception);
            _updateNote = "Could not reach update server. Check your connection.";
        }

        UpdateStatus.Text = _updateNote;
        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    // ============================= Diagnostics =============================

    private sealed record DiagnosticRow(string Label, string Value)
    {
        public override string ToString() => $"{Label}: {Value}";
    }

    private void RefreshDiagnostics()
    {
        if (_runtime is null) return;

        var settings = _runtime.Settings;
        DiagnosticsList.ItemsSource = new[]
        {
            new DiagnosticRow("Version", AppVersion.Current),
            new DiagnosticRow("AI Provider", settings.AiProvider),
            new DiagnosticRow("Plan Tier", $"{_runtime.Account.Plan}"),
            new DiagnosticRow("Screen Vision", settings.CaptureActiveWindow ? "Active" : "Off"),
            new DiagnosticRow("Speech Output", settings.SpeechEnabled ? settings.VoiceName : "Off"),
            new DiagnosticRow("Engine Status", _runtime.CurrentStatus),
            new DiagnosticRow("Log File Path", _runtime.LogPath)
        };

        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshDiagnostics_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        RefreshDiagnostics();
    }

    private void OpenLogs_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_runtime is null) return;

        try
        {
            Process.Start(new ProcessStartInfo(_runtime.LogPath) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _runtime.Log.Error("The log file could not be opened.", exception);
        }
    }

    private static void SelectCombo(ComboBox box, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (box.Items.Count > 0 && box.SelectedIndex < 0) box.SelectedIndex = 0;
            return;
        }

        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            var tag = item.Tag as string;
            var content = item.Content as string;
            if (string.Equals(tag, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(content, value, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(value) && content?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true))
            {
                item.IsSelected = true;
                box.SelectedItem = item;
                return;
            }
        }

        if (box.Items.Count > 0 && box.SelectedIndex < 0)
        {
            box.SelectedIndex = 0;
        }
    }

    private static string SelectedText(ComboBox box, string fallback)
    {
        if (box.SelectedItem is ComboBoxItem item)
        {
            return (item.Tag as string) ?? (item.Content as string) ?? fallback;
        }
        return fallback;
    }

    private static System.Windows.Media.SolidColorBrush Fill(string hex) =>
        (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;

    private System.Windows.Media.Brush Themed(string key, byte r, byte g, byte b) =>
        TryFindResource(key) as System.Windows.Media.Brush
        ?? Fill($"#{r:X2}{g:X2}{b:X2}");

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async void Save_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_runtime is null || _loading) return;

        SaveStatus.Text = "Saving…";

        try
        {
            var current = _runtime.Settings;
            var canBringOwn = _runtime.Can(MetisFeature.CustomAiProvider);

            var updated = current with
            {
                UserName = _section == "General" ? UserNameBox.Text.Trim() : current.UserName,
                StartWithWindows = _section == "General" ? StartWithWindowsCheck.IsChecked == true : current.StartWithWindows,
                ReduceMotion = _section == "General" ? ReduceMotionCheck.IsChecked == true : current.ReduceMotion,
                ActivationSoundsEnabled = _section == "General" ? SoundsCheck.IsChecked == true : current.ActivationSoundsEnabled,
                ThemePreference = _section == "General" ? _theme : current.ThemePreference,

                AiProvider = _section == "Intelligence" ? SelectedText(AiProviderBox, current.AiProvider) : current.AiProvider,
                ReasoningModel = _section == "Intelligence" && canBringOwn && !string.IsNullOrWhiteSpace(GeminiModelBox.Text) ? GeminiModelBox.Text.Trim() : current.ReasoningModel,
                OpenAiReasoningModel = _section == "Intelligence" && canBringOwn && !string.IsNullOrWhiteSpace(OpenAiModelBox.Text) ? OpenAiModelBox.Text.Trim() : current.OpenAiReasoningModel,
                ClaudeReasoningModel = _section == "Intelligence" && canBringOwn && !string.IsNullOrWhiteSpace(ClaudeModelBox.Text) ? ClaudeModelBox.Text.Trim() : current.ClaudeReasoningModel,
                OpenRouterModel = _section == "Intelligence" && canBringOwn && !string.IsNullOrWhiteSpace(OpenRouterModelBox.Text) ? OpenRouterModelBox.Text.Trim() : current.OpenRouterModel,
                OllamaEndpoint = _section == "Intelligence" && !string.IsNullOrWhiteSpace(OllamaEndpointBox.Text) ? OllamaEndpointBox.Text.Trim() : current.OllamaEndpoint,
                OllamaModel = _section == "Intelligence" && !string.IsNullOrWhiteSpace(OllamaModelBox.Text) ? OllamaModelBox.Text.Trim() : current.OllamaModel,

                SpeechEnabled = _section == "Voice" ? SpeechEnabledCheck.IsChecked == true : current.SpeechEnabled,
                SpeakErrorsAloud = _section == "Voice" ? SpeakErrorsCheck.IsChecked == true : current.SpeakErrorsAloud,
                VoiceName = _section == "Voice" ? SelectedText(GeminiVoiceBox, current.VoiceName) : current.VoiceName,
                SpeechToTextProvider = _section == "Voice" ? SelectedText(SpeechToTextProviderBox, current.SpeechToTextProvider) : current.SpeechToTextProvider,
                AssemblyAiModel = _section == "Voice" ? Fallback(AssemblyAiModelBox.Text, current.AssemblyAiModel) : current.AssemblyAiModel,
                WhisperCppExecutablePath = _section == "Voice" ? Fallback(WhisperCppExecutablePathBox.Text, current.WhisperCppExecutablePath) : current.WhisperCppExecutablePath,
                WhisperCppModelPath = _section == "Voice" ? Fallback(WhisperCppModelPathBox.Text, current.WhisperCppModelPath) : current.WhisperCppModelPath,
                PreferredMicrophoneId = _section == "Voice" ? MicrophoneBox.SelectedValue as string ?? current.PreferredMicrophoneId : current.PreferredMicrophoneId,

                UserSkillsEnabled = _section == "Skills" ? UserSkillsCheck.IsChecked == true : current.UserSkillsEnabled,
                SkillsFolder = _section == "Skills" ? SkillsFolderBox.Text.Trim() : current.SkillsFolder,

                CompanionAlwaysVisible = _section == "Companion" ? CompanionAlwaysCheck.IsChecked == true : current.CompanionAlwaysVisible,
                CompanionColor = _section == "Companion" ? _companionColour : current.CompanionColor,
                CompanionShape = _section == "Companion" ? _companionCharacter : current.CompanionShape,
                CompanionSize = _section == "Companion" ? (int)CompanionSizeSlider.Value : current.CompanionSize,
                CursorDistance = _section == "Companion" ? (int)CursorDistanceSlider.Value : current.CursorDistance,

                CaptureActiveWindow = _section == "Privacy" ? CaptureScreenCheck.IsChecked == true : current.CaptureActiveWindow,
                VisualGuidanceEnabled = _section == "Privacy" ? VisualGuidanceCheck.IsChecked == true : current.VisualGuidanceEnabled,
                ExcludedApplications = _section == "Privacy" ? ExcludedAppsBox.Text.Trim() : current.ExcludedApplications,
                MemoryEnabled = _section == "Privacy" ? MemoryEnabledCheck.IsChecked == true : current.MemoryEnabled,
                ChatMemoryEnabled = _section == "Privacy" ? ChatMemoryCheck.IsChecked == true : current.ChatMemoryEnabled,

                AgentAutonomyMode = _section == "Agents" ? _autonomy : current.AgentAutonomyMode,
                AgentMaxTurns = _section == "Agents" ? (int)AgentTurnsSlider.Value : current.AgentMaxTurns,
                AgentWindowsNotificationsEnabled = _section == "Agents" ? AgentNotificationsCheck.IsChecked == true : current.AgentWindowsNotificationsEnabled
            };

            if (_section == "Intelligence" && canBringOwn)
            {
                _runtime.SaveAdditionalProviderSecrets(
                    NullIfBlank(ClaudeApiKeyBox.Password),
                    null,
                    null,
                    null,
                    NullIfBlank(OpenRouterApiKeyBox.Password));

                await _runtime.SaveSettingsAsync(
                    updated,
                    NullIfBlank(GeminiApiKeyBox.Password),
                    NullIfBlank(OpenAiApiKeyBox.Password));

                GeminiApiKeyBox.Password = string.Empty;
                OpenAiApiKeyBox.Password = string.Empty;
                ClaudeApiKeyBox.Password = string.Empty;
                OpenRouterApiKeyBox.Password = string.Empty;
            }
            else if (_section == "Voice")
            {
                // The key lives in Windows Credential Manager, not in settings,
                // so it goes through the same path every other provider's does.
                _runtime.SaveAdditionalProviderSecrets(
                    null, null, NullIfBlank(AssemblyAiApiKeyBox.Password), null);

                await _runtime.SaveSettingsAsync(updated, null, null);
                AssemblyAiApiKeyBox.Password = string.Empty;
            }
            else
            {
                await _runtime.SaveSettingsAsync(updated, null, null);
            }

            SaveStatus.Text = $"Saved at {DateTime.Now:HH:mm}.";
        }
        catch (Exception exception)
        {
            SaveStatus.Text = "Could not save. " + exception.Message;
            _runtime.Log.Error("Could not save settings from the notch.", exception);
        }
    }
}
