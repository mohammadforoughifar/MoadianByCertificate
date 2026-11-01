# Office automation guides

All six guides (including the existing inner-letter guide) use `OfficeGuide.razor`
and its isolated stylesheet. `::deep` is intentional: content is projected through
`ChildContent`, including `OfficeGuideSection` children. Do not move those rules
back to an individual guide or duplicate the stylesheet.

## Automated source and JavaScript checks

From the repository root, no extra packages required:

```sh
python3 -m unittest discover -s inventory/tests/OfficeGuides -v
node --test inventory/tests/OfficeGuides/print-guide.test.cjs
```

The source contracts check module entry points, distinct email content, complete
and unique TOC targets, shared design, and print/mobile hooks. The JavaScript tests
check expanding FAQ answers for print and restoring their original state after
printing or errors, plus instance-local TOC scrolling for cached workspace tabs. These are **not** a substitute for compiling Razor:

```sh
dotnet build inventory/src/Inventory.Client/Inventory.Client.csproj
```

## Browser regression checklist

- In incoming/outgoing letters, open help from a filtered list and from a reader.
  The guide replaces list/reader; Back restores the prior folder/filter/page.
  Another folder button exits help normally. Opening help must not issue a list
  request using a fabricated `guide` folder or trigger email synchronization.
- In the secretariat, test both outgoing and incoming modes. Open help, return,
  and verify incoming filters and selections survive. The incoming panel stays
  mounted so navigation refs remain usable.
- Open both email routes, including switching directly between them. Verify the
  title and guide content match personal/organizational mode. Help must work with
  no configured accounts and for read-only users.
- Test at desktop, tablet and narrow mobile widths: no horizontal page overflow,
  scrollable guide, visible rail entry, clickable TOC, and accessible Back buttons.
- Print/save PDF: no rail or mailbox; all sections and FAQ answers are included,
  without clipping to the shell height. Cancel printing and confirm previously
  collapsed FAQs are collapsed again.
- Confirm the inner-letter guide still has its original twelve content sections.

## Validation in this workspace

Source and JavaScript checks pass. Static-render browser checks also passed for
all six guides at 1440, 768 and 390px, including scoped styling, TOC scrolling,
print overflow and FAQ restoration. Static rendering used the production markup
and CSS but simulated Razor's scope attribute; it did **not** execute Blazor.
The client build could not run because the workspace has no .NET SDK and SDK
endpoints were unavailable. Run the build and authenticated integration checklist
in a .NET-enabled environment before release.
