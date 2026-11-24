# Print layouts (0.23.0)

Each organization can make its printed papers look like its own forms:

- the **job card**, the **purchase order**, a new **request receipt**, and **labels**;
- which **blocks** print and in what order, and which **fields** each block shows;
- **header**, **footer** and **terms** text in English and Arabic;
- the **paper size** (A4, A5, Letter) and the **logo**, or for labels the **label size**;
- **several layouts per paper**, chosen by site and type, with a default.

With no layout, every paper prints exactly as it did before 0.23.0.

## Who may do what

| Permission | Can |
| --- | --- |
| *Manage settings* | Add, change and delete layouts (Settings › Print layouts). |
| Anyone who can print a record | Print it, and pick another layout of that paper when printing. |

There are no new permissions.

## Setting up a layout

**Settings › Print layouts** has a tab for each paper. The first row of each is **Built-in**: today's paper, which can't be
changed. **Copy** it (or any layout) to start a new one.

In the layout window:

- **Name**, and **Site** and **type** (work type for the job card, order type for the purchase order, request type for the
  receipt, assets/locations/parts for labels), both optional.
- **Default layout**: prints when no layout for the record's site or type applies. It has no site or type.
- **Paper** (A4, A5 or Letter) and **Organization logo** (the logo from Settings › Organization).
- **Header text**, **Footer text** and **Terms**, each in English and Arabic. They are printed as typed, line breaks kept.
  Header and footer take up to 500 characters each, terms up to 4,000.
- **What prints, in order**: tick a block to print it and move it up or down; tick the fields it shows. The header always comes
  first. On the purchase order the description, the line total and the total always print.
- **Asset fields** (job card): your asset custom fields, chosen one by one.
- For labels: the **label size** (today's A4 sheet of 3 × 8, two other A4 sheets, or one label per page for 50 × 25 mm and
  100 × 50 mm label printers) and what each label shows (number, name, the third line, site). Every label keeps its QR code.

The **preview** beside the window shows the latest record printed with the layout as it is now, before saving. Type a record's
number to preview another.

Up to 20 layouts per paper.

## Which layout prints

For each record, the most specific layout that applies:

1. one for the record's site and type;
2. one for its site;
3. one for its type;
4. the default;
5. the built-in paper.

Layouts with no site, no type and not the default never apply by themselves; they are there to pick by hand, for example a
"Short A5 card" or a "Contractor card".

Every print page has a **Layout** choice listing the paper's layouts, the automatic one first. The choice stays in the page's
address, so reopening or bookmarking it keeps it.

Labels for several records go on one sheet of one size: when the records are at sites with different layouts, the first one's
layout decides, and the page says so.

## The purchase order e-mail

**Send to supplier** builds the e-mail from the order's layout: the header text, the supplier and order details shown, the line
columns and totals shown, notes, terms and footer, in the organization's language. When the discount column is hidden, the unit
price shown is the price after discount, so quantity × price equals the line total. With no layout, the e-mail is the same as
before.

## Language

A layout's texts print in the page's language: the Arabic text on an Arabic page when it is filled in, otherwise the English
text. The supplier e-mail uses the organization's language.

## The request receipt

A request's page has **Print receipt**: the request's number, what was asked, by whom, when the work can be done and the work
order that followed, for the requester or a noticeboard.

## What stays the same

- "Save as PDF" is the browser's print dialog; CMX doesn't make PDF files on the server.
- The phone doesn't print.
