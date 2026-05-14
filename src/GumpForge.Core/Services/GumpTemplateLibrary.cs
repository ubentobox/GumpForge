using GumpForge.Core.Models;

namespace GumpForge.Core.Services;

/// <summary>
/// Provides pre-built gump templates as GumpDocuments.
/// Templates include common UO UI patterns like dialogs, vendors, skill menus, etc.
/// </summary>
public static class GumpTemplateLibrary
{
    public static List<GumpTemplate> GetTemplates() =>
    [
        new GumpTemplate
        {
            Name = "Blank Canvas",
            Description = "Empty 600×400 canvas",
            Category = "Basic",
            CreateDocument = () => new GumpDocument
            {
                GumpClassName = "CustomGump",
                CanvasWidth = 600,
                CanvasHeight = 400
            }
        },
        new GumpTemplate
        {
            Name = "Simple Dialog",
            Description = "Background with title, text area, and OK/Cancel buttons",
            Category = "Basic",
            CreateDocument = CreateSimpleDialog
        },
        new GumpTemplate
        {
            Name = "Confirmation Dialog",
            Description = "Yes/No confirmation with icon",
            Category = "Basic",
            CreateDocument = CreateConfirmationDialog
        },
        new GumpTemplate
        {
            Name = "Tabbed Panel",
            Description = "Multi-page gump with tab buttons",
            Category = "Multi-Page",
            CreateDocument = CreateTabbedPanel
        },
        new GumpTemplate
        {
            Name = "Scrollable List",
            Description = "List with scrollable HTML area and buttons",
            Category = "Data Display",
            CreateDocument = CreateScrollableList
        },
        new GumpTemplate
        {
            Name = "Vendor Menu",
            Description = "Grid layout for buy/sell items",
            Category = "Commerce",
            CreateDocument = CreateVendorMenu
        },
        new GumpTemplate
        {
            Name = "Input Form",
            Description = "Form with labeled text entry fields",
            Category = "Data Entry",
            CreateDocument = CreateInputForm
        },
        new GumpTemplate
        {
            Name = "Settings Panel",
            Description = "Checkbox/radio options panel",
            Category = "Data Entry",
            CreateDocument = CreateSettingsPanel
        }
    ];

    private static GumpDocument CreateSimpleDialog()
    {
        var doc = new GumpDocument
        {
            GumpClassName = "SimpleDialogGump",
            CanvasWidth = 400,
            CanvasHeight = 350
        };

        var page = doc.GetOrCreatePage(0);

        // Background
        page.Elements.Add(new GumpBackground
        {
            X = 0, Y = 0, Width = 360, Height = 300,
            GumpId = 9200, Name = "Background"
        });

        // Title label
        page.Elements.Add(new GumpLabel
        {
            X = 30, Y = 20, Width = 200, Height = 20,
            Hue = 0x480, Text = "Dialog Title", Name = "Title"
        });

        // HTML content area
        page.Elements.Add(new GumpHtml
        {
            X = 30, Y = 50, Width = 300, Height = 180,
            Text = "Your dialog content goes here.",
            HasBackground = true, HasScrollbar = false, Name = "Content"
        });

        // OK button
        page.Elements.Add(new GumpButton
        {
            X = 30, Y = 250, Width = 40, Height = 40,
            NormalId = 4005, PressedId = 4007,
            ButtonId = 1, ButtonType = GumpButtonType.Reply,
            Name = "OkButton"
        });
        page.Elements.Add(new GumpLabel
        {
            X = 75, Y = 252, Width = 40, Height = 20,
            Hue = 0x480, Text = "OK", Name = "OkLabel"
        });

        // Cancel button
        page.Elements.Add(new GumpButton
        {
            X = 200, Y = 250, Width = 40, Height = 40,
            NormalId = 4017, PressedId = 4019,
            ButtonId = 0, ButtonType = GumpButtonType.Reply,
            Name = "CancelButton"
        });
        page.Elements.Add(new GumpLabel
        {
            X = 245, Y = 252, Width = 60, Height = 20,
            Hue = 0x480, Text = "Cancel", Name = "CancelLabel"
        });

        return doc;
    }

    private static GumpDocument CreateConfirmationDialog()
    {
        var doc = new GumpDocument
        {
            GumpClassName = "ConfirmGump",
            CanvasWidth = 350,
            CanvasHeight = 250
        };

        var page = doc.GetOrCreatePage(0);

        page.Elements.Add(new GumpBackground
        {
            X = 0, Y = 0, Width = 300, Height = 200,
            GumpId = 9200, Name = "Background"
        });

        page.Elements.Add(new GumpLabel
        {
            X = 30, Y = 20, Width = 200, Height = 20,
            Hue = 0x22, Text = "Confirmation", Name = "Title"
        });

        page.Elements.Add(new GumpHtml
        {
            X = 30, Y = 50, Width = 240, Height = 80,
            Text = "Are you sure you want to proceed?",
            HasBackground = false, HasScrollbar = false, Name = "Message"
        });

        // Yes
        page.Elements.Add(new GumpButton
        {
            X = 30, Y = 140, Width = 40, Height = 40,
            NormalId = 4005, PressedId = 4007,
            ButtonId = 1, ButtonType = GumpButtonType.Reply,
            Name = "YesButton"
        });
        page.Elements.Add(new GumpLabel
        {
            X = 75, Y = 142, Width = 40, Height = 20,
            Hue = 0x480, Text = "Yes", Name = "YesLabel"
        });

        // No
        page.Elements.Add(new GumpButton
        {
            X = 160, Y = 140, Width = 40, Height = 40,
            NormalId = 4017, PressedId = 4019,
            ButtonId = 0, ButtonType = GumpButtonType.Reply,
            Name = "NoButton"
        });
        page.Elements.Add(new GumpLabel
        {
            X = 205, Y = 142, Width = 40, Height = 20,
            Hue = 0x480, Text = "No", Name = "NoLabel"
        });

        return doc;
    }

    private static GumpDocument CreateTabbedPanel()
    {
        var doc = new GumpDocument
        {
            GumpClassName = "TabbedPanelGump",
            CanvasWidth = 500,
            CanvasHeight = 450
        };

        var page0 = doc.GetOrCreatePage(0);

        // Background (shared across all pages)
        page0.Elements.Add(new GumpBackground
        {
            X = 0, Y = 0, Width = 450, Height = 400,
            GumpId = 9200, Name = "Background"
        });

        // Tab buttons on page 0
        for (int i = 1; i <= 3; i++)
        {
            page0.Elements.Add(new GumpButton
            {
                X = 20 + (i - 1) * 130, Y = 10, Width = 120, Height = 25,
                NormalId = 4005, PressedId = 4007,
                ButtonId = i, ButtonType = GumpButtonType.Page,
                Param = i, Name = $"Tab{i}Button"
            });
            page0.Elements.Add(new GumpLabel
            {
                X = 45 + (i - 1) * 130, Y = 12, Width = 80, Height = 20,
                Hue = 0x480, Text = $"Tab {i}", Name = $"Tab{i}Label"
            });
        }

        // Content for each tab page
        for (int i = 1; i <= 3; i++)
        {
            var page = doc.GetOrCreatePage(i);
            page.Elements.Add(new GumpHtml
            {
                X = 30, Y = 50, Width = 390, Height = 310,
                Text = $"Content for Tab {i}",
                HasBackground = true, HasScrollbar = true,
                Name = $"Tab{i}Content"
            });
        }

        return doc;
    }

    private static GumpDocument CreateScrollableList()
    {
        var doc = new GumpDocument
        {
            GumpClassName = "ScrollableListGump",
            CanvasWidth = 500,
            CanvasHeight = 500
        };

        var page = doc.GetOrCreatePage(0);

        page.Elements.Add(new GumpBackground
        {
            X = 0, Y = 0, Width = 450, Height = 450,
            GumpId = 9200, Name = "Background"
        });

        page.Elements.Add(new GumpLabel
        {
            X = 30, Y = 15, Width = 200, Height = 20,
            Hue = 0x480, Text = "Item List", Name = "Title"
        });

        // Scrollable list area
        page.Elements.Add(new GumpHtml
        {
            X = 20, Y = 45, Width = 410, Height = 350,
            Text = "<basefont color=#FFFFFF>Item 1<br>Item 2<br>Item 3<br>Item 4<br>Item 5",
            HasBackground = true, HasScrollbar = true,
            Name = "ListArea"
        });

        // Close button
        page.Elements.Add(new GumpButton
        {
            X = 190, Y = 405, Width = 40, Height = 40,
            NormalId = 4017, PressedId = 4019,
            ButtonId = 0, ButtonType = GumpButtonType.Reply,
            Name = "CloseButton"
        });

        return doc;
    }

    private static GumpDocument CreateVendorMenu()
    {
        var doc = new GumpDocument
        {
            GumpClassName = "VendorMenuGump",
            CanvasWidth = 500,
            CanvasHeight = 500
        };

        var page = doc.GetOrCreatePage(0);

        page.Elements.Add(new GumpBackground
        {
            X = 0, Y = 0, Width = 450, Height = 450,
            GumpId = 9200, Name = "Background"
        });

        page.Elements.Add(new GumpLabel
        {
            X = 30, Y = 15, Width = 200, Height = 20,
            Hue = 0x480, Text = "Vendor Wares", Name = "Title"
        });

        // Grid of item slots (3x3)
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int x = 30 + col * 130;
                int y = 50 + row * 120;
                int slot = row * 3 + col + 1;

                // Item frame
                page.Elements.Add(new GumpImageTiled
                {
                    X = x, Y = y, Width = 110, Height = 100,
                    GumpId = 9350, Name = $"Slot{slot}Frame"
                });

                // Buy button
                page.Elements.Add(new GumpButton
                {
                    X = x + 70, Y = y + 75, Width = 40, Height = 20,
                    NormalId = 4005, PressedId = 4007,
                    ButtonId = slot, ButtonType = GumpButtonType.Reply,
                    Name = $"Buy{slot}Button"
                });

                // Price label
                page.Elements.Add(new GumpLabel
                {
                    X = x + 5, Y = y + 78, Width = 60, Height = 20,
                    Hue = 0x480, Text = $"{slot * 100}gp", Name = $"Price{slot}"
                });
            }
        }

        return doc;
    }

    private static GumpDocument CreateInputForm()
    {
        var doc = new GumpDocument
        {
            GumpClassName = "InputFormGump",
            CanvasWidth = 450,
            CanvasHeight = 400
        };

        var page = doc.GetOrCreatePage(0);

        page.Elements.Add(new GumpBackground
        {
            X = 0, Y = 0, Width = 400, Height = 350,
            GumpId = 9200, Name = "Background"
        });

        page.Elements.Add(new GumpLabel
        {
            X = 30, Y = 15, Width = 200, Height = 20,
            Hue = 0x480, Text = "Registration Form", Name = "Title"
        });

        // Form fields
        string[] labels = ["Name", "Guild", "Title"];
        for (int i = 0; i < labels.Length; i++)
        {
            int y = 55 + i * 50;
            page.Elements.Add(new GumpLabel
            {
                X = 30, Y = y, Width = 80, Height = 20,
                Hue = 0x480, Text = labels[i], Name = $"Label{i}"
            });
            page.Elements.Add(new GumpTextEntry
            {
                X = 120, Y = y, Width = 250, Height = 25,
                Hue = 0x480, EntryId = i,
                InitialText = "", Name = $"Entry{i}"
            });
        }

        // Submit
        page.Elements.Add(new GumpButton
        {
            X = 30, Y = 280, Width = 40, Height = 40,
            NormalId = 4005, PressedId = 4007,
            ButtonId = 1, ButtonType = GumpButtonType.Reply,
            Name = "SubmitButton"
        });
        page.Elements.Add(new GumpLabel
        {
            X = 75, Y = 282, Width = 60, Height = 20,
            Hue = 0x480, Text = "Submit", Name = "SubmitLabel"
        });

        return doc;
    }

    private static GumpDocument CreateSettingsPanel()
    {
        var doc = new GumpDocument
        {
            GumpClassName = "SettingsGump",
            CanvasWidth = 450,
            CanvasHeight = 450
        };

        var page = doc.GetOrCreatePage(0);

        page.Elements.Add(new GumpBackground
        {
            X = 0, Y = 0, Width = 400, Height = 400,
            GumpId = 9200, Name = "Background"
        });

        page.Elements.Add(new GumpLabel
        {
            X = 30, Y = 15, Width = 200, Height = 20,
            Hue = 0x480, Text = "Settings", Name = "Title"
        });

        // Checkbox options
        string[] options = ["Enable Notifications", "Auto-Save", "Show Tooltips", "Dark Mode"];
        for (int i = 0; i < options.Length; i++)
        {
            int y = 55 + i * 40;
            page.Elements.Add(new GumpCheck
            {
                X = 30, Y = y, Width = 30, Height = 30,
                InactiveId = 210, ActiveId = 211,
                InitialState = false, SwitchId = i + 1,
                Name = $"Check{i}"
            });
            page.Elements.Add(new GumpLabel
            {
                X = 65, Y = y + 5, Width = 200, Height = 20,
                Hue = 0x480, Text = options[i], Name = $"CheckLabel{i}"
            });
        }

        // Radio group
        page.Elements.Add(new GumpLabel
        {
            X = 30, Y = 225, Width = 200, Height = 20,
            Hue = 0x22, Text = "Difficulty:", Name = "RadioTitle"
        });

        string[] radioLabels = ["Easy", "Normal", "Hard"];
        for (int i = 0; i < radioLabels.Length; i++)
        {
            int y = 250 + i * 35;
            page.Elements.Add(new GumpRadio
            {
                X = 30, Y = y, Width = 30, Height = 30,
                InactiveId = 210, ActiveId = 211,
                InitialState = i == 1, SwitchId = 100 + i,
                Name = $"Radio{i}"
            });
            page.Elements.Add(new GumpLabel
            {
                X = 65, Y = y + 5, Width = 80, Height = 20,
                Hue = 0x480, Text = radioLabels[i], Name = $"RadioLabel{i}"
            });
        }

        // Apply
        page.Elements.Add(new GumpButton
        {
            X = 30, Y = 350, Width = 40, Height = 40,
            NormalId = 4005, PressedId = 4007,
            ButtonId = 1, ButtonType = GumpButtonType.Reply,
            Name = "ApplyButton"
        });
        page.Elements.Add(new GumpLabel
        {
            X = 75, Y = 352, Width = 60, Height = 20,
            Hue = 0x480, Text = "Apply", Name = "ApplyLabel"
        });

        return doc;
    }
}

/// <summary>
/// Descriptor for a gump template.
/// </summary>
public class GumpTemplate
{
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = "General";
    public required Func<GumpDocument> CreateDocument { get; init; }
}
