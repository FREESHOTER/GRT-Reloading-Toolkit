using System.Globalization;
using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Cartridges;
using GrtReloadingToolkit.Measuring;

namespace GrtReloadingToolkit.Ui;

internal sealed partial class CartridgesForm
{
    /// <summary>
    /// The lots of one purpose and their cartridges. The match lots are the ones kept for the match; the training lots are other cartridges, made to
    /// shoot in training (to teach the model, to try a plan, to check a prediction) so that the match lot is never touched. The training page also says which
    /// match lot a training lot imitates, and whether what the model learned with it can be used for that match lot.
    /// </summary>
    private sealed class LotsPane
    {
        private readonly CartridgesForm _f;
        private readonly string _purpose;
        private bool IsTraining => _purpose == "training";
        private ArchiveDb Db => _f.Db;
        private long Shooter => _f.Shooter;

        private readonly ListView _lots = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
        private readonly DataGridView _carts = Grid();
        private readonly Label _cartHint = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 4, 4, 0), ForeColor = SystemColors.GrayText };
        private readonly Label _purposeHint = new() { Dock = DockStyle.Top, Height = 110, Padding = new Padding(4, 4, 4, 0), ForeColor = SystemColors.GrayText };
        private readonly ComboBox _reference = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        private readonly TextBox _transfer = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly ProgressChart _progress = new() { Dock = DockStyle.Right, Width = 430, Visible = false };
        private readonly Panel _bottom = new() { Dock = DockStyle.Bottom, Height = 190 };
        private List<CartridgeLotRow> _lotRows = new();
        private List<CartridgeLotRow> _matchLots = new();
        private List<CartridgeRow> _cartRows = new();
        private Dictionary<long, string> _pieceMarks = new();
        private bool _filling;

        public LotsPane(CartridgesForm form, string purpose)
        {
            _f = form;
            _purpose = purpose;
        }

        public Control Build()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill }.WithDistance(IsTraining ? 340 : 300);

            _lots.Columns.Add(Lang.T("Lot"), 190);
            _lots.Columns.Add(Lang.T("Cartridges"), 80);
            if (IsTraining) _lots.Columns.Add(Lang.T("Imitates"), 120);
            var lotBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = IsTraining ? 78 : 40, Padding = new Padding(2, 6, 2, 0), WrapContents = true };
            var newLot = new Button { Text = Lang.T(IsTraining ? "New training lot…" : "New match lot…"), AutoSize = true };
            var delLot = new Button { Text = Lang.T("Delete lot"), AutoSize = true };
            var move = new Button { Text = Lang.T(IsTraining ? "Make it a match lot" : "Make it a training lot"), AutoSize = true };
            lotBar.Controls.AddRange(new Control[] { newLot, delLot, move });
            newLot.Click += (_, _) => NewLot();
            delLot.Click += (_, _) => DeleteLot();
            move.Click += (_, _) => MoveLot();
            _lots.SelectedIndexChanged += (_, _) => { FillCartridges(); FillReference(); };
            split.Panel1.Controls.Add(_lots);
            split.Panel1.Controls.Add(lotBar);
            split.Panel1.Controls.Add(_purposeHint);
            _purposeHint.Text = Lang.T(IsTraining
                ? "Training lots are other cartridges than the match's: shoot them to teach the model what moves the velocity, to try a plan and to check a prediction. They are never the lot you take to the match."
                : "The match lot is kept for the match: do not shoot it in training. Make other cartridges, in the Training tab, to learn and to try things out.");

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(2, 6, 2, 0), WrapContents = false };
            var add = new Button { Text = Lang.T("Add cartridges…"), AutoSize = true };
            var del = new Button { Text = Lang.T("Delete selected"), AutoSize = true };
            bar.Controls.AddRange(new Control[] { add, del });
            if (IsTraining)
            {
                bar.Controls.Add(new Label { Text = Lang.T("Imitates the match lot:"), AutoSize = true, Margin = new Padding(16, 6, 4, 0) });
                bar.Controls.Add(_reference);
                var compare = new Button { Text = Lang.T("Compare with the match lot"), AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
                compare.Click += (_, _) => Compare();
                bar.Controls.Add(compare);
                var taught = new Button { Text = Lang.T("What the training taught"), AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
                taught.Click += (_, _) => Taught();
                bar.Controls.Add(taught);
                _reference.SelectionChangeCommitted += (_, _) => ReferenceChosen();
            }
            add.Click += (_, _) => AddCartridges();
            del.Click += (_, _) => DeleteCartridges();
            _cartHint.Text = Lang.T("Type the mark of the case, the bullet and the primer of each cartridge (a mark that is not there yet is created). The measures of the pieces come with them; here you add the ones taken on the finished cartridge. Right-click the title of a column to change its unit.");
            _carts.CellEndEdit += (_, e) => CartridgeEdited(e.RowIndex, e.ColumnIndex);
            AppUnits.AttachColumnMenu(_carts, FillCartridges);               // a right click on the title of a column changes its unit
            _carts.CurrentCellDirtyStateChanged += (_, _) => { if (_carts.CurrentCell is DataGridViewCheckBoxCell) _carts.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            split.Panel2.Controls.Add(_carts);
            if (IsTraining) { _bottom.Controls.Add(_transfer); _bottom.Controls.Add(_progress); split.Panel2.Controls.Add(_bottom); }
            split.Panel2.Controls.Add(_cartHint);
            split.Panel2.Controls.Add(bar);
            return split;
        }

        public void Fill()
        {
            long? keep = Selected?.Id;
            var all = CartridgeStore.Lots(Db, Shooter);
            _matchLots = all.Where(l => !l.IsTraining).ToList();
            _lotRows = all.Where(l => l.Purpose == _purpose).ToList();
            _lots.BeginUpdate();
            _lots.Items.Clear();
            foreach (var l in _lotRows)
            {
                var cells = new List<string> { l.Name, l.Cartridges.ToString(Inv) };
                if (IsTraining) cells.Add(l.ReferenceLotId is { } r ? _matchLots.FirstOrDefault(m => m.Id == r)?.Name ?? "" : "");
                _lots.Items.Add(new ListViewItem(cells.ToArray()));
            }
            _lots.EndUpdate();
            int at = keep is { } k ? _lotRows.FindIndex(l => l.Id == k) : -1;
            if (at < 0 && _lotRows.Count > 0) at = 0;
            if (at >= 0) _lots.Items[at].Selected = true; else { FillCartridges(); FillReference(); }
        }

        private CartridgeLotRow? Selected => _lots.SelectedIndices.Count == 0 ? null : _lotRows[_lots.SelectedIndices[0]];

        private void FillReference()
        {
            if (!IsTraining) return;
            _reference.Items.Clear();
            _reference.Items.Add(Lang.T("(none)"));
            foreach (var m in _matchLots) _reference.Items.Add(m.Name);
            int at = Selected?.ReferenceLotId is { } r ? _matchLots.FindIndex(m => m.Id == r) : -1;
            _reference.SelectedIndex = at + 1;
            _reference.Enabled = Selected != null;
            _transfer.Text = ""; _progress.Visible = false;
        }

        private void ReferenceChosen()
        {
            if (Selected is not { } lot) return;
            long? reference = _reference.SelectedIndex > 0 ? _matchLots[_reference.SelectedIndex - 1].Id : null;
            if (!_f.Guard(() => CartridgeStore.SetReference(Db, lot.Id, reference))) return;
            Fill();
        }

        /// <param name="quiet">The picture of the window (<c>--anteprima</c>) must never stop on a message: it just shows nothing when there is nothing to compare.</param>
        public void Compare(bool quiet = false)
        {
            if (Selected is not { } lot) { if (!quiet) _f.Warn(Lang.T("Make or pick a lot first.")); return; }
            if (lot.ReferenceLotId is not { } refId) { if (!quiet) _f.Warn(Lang.T("Say which match lot this training lot imitates first.")); return; }
            _f.Learn();
            if (_f._model == null) { _progress.Visible = false; _transfer.Text = Lang.T("No shot of the archive is linked to a measured cartridge yet. Link the shots of a session to their cartridges in the \"Check after the shooting\" tab."); return; }
            var race = CartridgeStore.CartridgeInputs(Db, refId);
            var training = CartridgeStore.CartridgeInputs(Db, lot.Id);
            var res = TrainingTransfer.Compare(_f._model, race, training);
            var provenance = _f._set is { } s
                ? Tr.F("The model learned from {0} shots: {1} fired from training lots, {2} from match lots.", _f._model.NShots, s.TrainingShots, s.RaceShots) + Environment.NewLine + Environment.NewLine
                : "";
            _progress.Visible = false;
            _transfer.Text = provenance + TrainingTransfer.Text(res);
        }

        /// <summary>What the latest training session changed in the model, and what that changes for the match lot.</summary>
        public void Taught()
        {
            var set = CartridgeStore.LearningShots(Db, Shooter);
            long? lotId = Selected?.ReferenceLotId ?? _matchLots.FirstOrDefault(m => m.Cartridges > 0)?.Id;
            var lot = lotId is { } id ? CartridgeStore.CartridgeInputs(Db, id) : null;
            var report = TrainingProgress.Compute(set, lot);
            _progress.SetData(report);
            _progress.Visible = report != null;
            _transfer.Text = report == null
                ? Lang.T("No shot fired with a training lot is linked to its cartridges yet. Link the shots of a training session in the \"Check after the shooting\" tab.")
                : TrainingProgress.Text(report);
        }

        private void MoveLot()
        {
            if (Selected is not { } lot) return;
            string to = IsTraining ? "race" : "training";
            if (!_f.Guard(() => CartridgeStore.SetPurpose(Db, lot.Id, to))) return;
            _f.RefreshLotPanes();
        }

        private void FillCartridges()
        {
            _filling = true;
            _carts.Columns.Clear();
            _carts.Rows.Clear();
            _carts.Columns.Add(new DataGridViewTextBoxColumn { Name = "n", HeaderText = Lang.T("No."), ReadOnly = true, Width = 50 });
            foreach (var (n, h) in new[] { ("case", "Case"), ("bullet", "Bullet"), ("primer", "Primer") })
                _carts.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = Lang.T(h), Width = 70 });
            _carts.Columns.Add(new DataGridViewTextBoxColumn { Name = "target", HeaderText = Lang.T("Target"), Width = 60 });
            _carts.Columns.Add(new DataGridViewCheckBoxColumn { Name = "out", HeaderText = Lang.T("Out of spec"), Width = 70 });
            foreach (string f in CartridgeFields)
                _carts.Columns.Add(new DataGridViewTextBoxColumn { Name = f, HeaderText = Header(f), Width = 120 });
            if (Selected is { } lot)
            {
                _pieceMarks = CartridgeStore.Pieces(Db, Shooter).ToDictionary(p => p.Id, p => p.Mark);
                _cartRows = CartridgeStore.Cartridges(Db, lot.Id);
                foreach (var c in _cartRows)
                {
                    var m = CartridgeStore.Measures(Db, "cartridge", c.Id);
                    var cells = new List<object?> { c.Number, Mark(c.CasePieceId), Mark(c.BulletPieceId), Mark(c.PrimerPieceId), c.Target, c.OutOfSpec };
                    foreach (string f in CartridgeFields) cells.Add(m.TryGetValue(f, out double v) ? AppUnits.ShowMeasure(f, v) : "");
                    int i = _carts.Rows.Add(cells.ToArray());
                    _carts.Rows[i].Tag = c.Id;
                }
            }
            _filling = false;
        }

        private string Mark(long? pieceId) => pieceId is { } id && _pieceMarks.TryGetValue(id, out string? m) ? m : "";

        private void NewLot()
        {
            string? name = _f.Ask(Lang.T(IsTraining ? "New training lot" : "New match lot"), Lang.T("Name of the lot:"), "");
            if (name == null) return;
            long? reference = IsTraining && SelectedMatchForNewTraining() is { } m ? m : null;
            if (_f.Guard(() => CartridgeStore.CreateLot(Db, Shooter, name, purpose: _purpose, referenceLotId: reference))) { Fill(); _lots.Items[0].Selected = true; }
        }

        /// <summary>A new training lot imitates the only match lot there is, when there is just one.</summary>
        private long? SelectedMatchForNewTraining() => _matchLots.Count == 1 ? _matchLots[0].Id : null;

        private void DeleteLot()
        {
            if (Selected is not { } lot) return;
            if (MessageBox.Show(_f, string.Format(Inv, Lang.T("Delete the lot \"{0}\" with its {1} cartridges?"), lot.Name, lot.Cartridges),
                    _f.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (_f.Guard(() => CartridgeStore.DeleteLot(Db, lot.Id))) _f.RefreshLotPanes();
        }

        private void AddCartridges()
        {
            if (Selected is not { } lot) { _f.Warn(Lang.T("Make or pick a lot first.")); return; }
            string? text = _f.Ask(Lang.T("Add cartridges"), Lang.T("How many cartridges to add:"), "10");
            if (text == null) return;
            if (!int.TryParse(text, NumberStyles.Integer, Inv, out int n) || n < 1 || n > 500) { _f.Warn(Lang.T("That is not a number.")); return; }
            _f.Guard(() => Db.InTransaction(() =>
            {
                int next = CartridgeStore.NextNumber(Db, lot.Id);
                for (int i = 0; i < n; i++) CartridgeStore.AddCartridge(Db, lot.Id, next + i);
            }));
            Fill();
        }

        private void DeleteCartridges()
        {
            var rows = _carts.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex).Distinct().ToList();
            if (rows.Count == 0) return;
            if (MessageBox.Show(_f, string.Format(Inv, Lang.T("Delete {0} cartridges? Their shots stay, no longer linked to a cartridge."), rows.Count),
                    _f.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _f.Guard(() => { foreach (int r in rows) CartridgeStore.DeleteCartridge(Db, (long)_carts.Rows[r].Tag!); });
            Fill();
        }

        private void CartridgeEdited(int row, int col)
        {
            if (_filling || row < 0 || col < 1) return;
            long id = (long)_carts.Rows[row].Tag!;
            string name = _carts.Columns[col].Name;
            string text = _carts[col, row].Value?.ToString()?.Trim() ?? "";
            var c = _cartRows.First(x => x.Id == id);
            if (name is "case" or "bullet" or "primer" or "target" or "out")
            {
                long? Piece(string kind, string cell) => string.IsNullOrWhiteSpace(cell) ? null : CartridgeStore.FindOrAddPiece(Db, Shooter, kind, cell);
                long? cs = null, bl = null, pr = null;
                bool ok = _f.Guard(() =>
                {
                    cs = Piece("case", _carts["case", row].Value?.ToString() ?? "");
                    bl = Piece("bullet", _carts["bullet", row].Value?.ToString() ?? "");
                    pr = Piece("primer", _carts["primer", row].Value?.ToString() ?? "");
                    CartridgeStore.UpdateCartridge(Db, id, cs, bl, pr, (_carts["target", row].Value?.ToString() ?? "").Trim(), _carts["out", row].Value is true, c.OutReason);
                });
                if (!ok) return;
                _pieceMarks = CartridgeStore.Pieces(Db, Shooter).ToDictionary(p => p.Id, p => p.Mark);   // a mark typed any way shows as it is stored
                _filling = true;
                _carts["case", row].Value = Mark(cs); _carts["bullet", row].Value = Mark(bl); _carts["primer", row].Value = Mark(pr);
                _filling = false;
                _cartRows = _cartRows.Select(x => x.Id == id ? CartridgeStore.Cartridges(Db, x.LotId).First(y => y.Id == id) : x).ToList();
                return;
            }
            if (string.IsNullOrWhiteSpace(text)) { _f.Guard(() => CartridgeStore.DeleteMeasure(Db, "cartridge", id, name)); return; }
            if (!CartridgeText.TryParseNumber(text, out double v))
            {
                _f.Warn(Lang.T("That is not a number."));
                var old = CartridgeStore.Measures(Db, "cartridge", id);
                _carts[col, row].Value = old.TryGetValue(name, out double o) ? AppUnits.ShowMeasure(name, o) : "";
                return;
            }
            double stored = AppUnits.MeasureToStored(name, v);              // what was typed is in the unit of the column; the archive keeps its own
            if (_f.Guard(() => CartridgeStore.SetMeasure(Db, "cartridge", id, name, stored))) _carts[col, row].Value = AppUnits.ShowMeasure(name, stored);
        }
    }
}
