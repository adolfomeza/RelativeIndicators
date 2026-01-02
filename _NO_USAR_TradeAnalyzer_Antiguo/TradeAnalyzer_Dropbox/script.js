document.addEventListener('DOMContentLoaded', () => {
    console.log("Trade Analyzer Script Loaded v1.2");
    // alert("Analyzer Script Loaded - If you see this, cache is cleared."); // Uncomment if desperate

    const dropZone = document.getElementById('drop-zone');
    const fileInput = document.getElementById('file-input');
    const dashboard = document.getElementById('dashboard');
    
    // Global Data Store for Incremental Loading
    let globalAllTrades = [];
    
    // Global Chart Settings
    let currentPeriod = 'D'; // D, W, M, Y
    let currentChartType = 'bar'; // bar, line
    
    // Chart Instances
    const charts = {};

    // Colors for Multi-Instrument Chart
    const COLORS = [
        '#10b981', // Green
        '#f59e0b', // Amber
        '#8b5cf6', // Violet
        '#ec4899', // Pink
        '#6366f1', // Indigo
        '#14b8a6', // Teal
        '#ef4444', // Red
        '#3b82f6', // Blue
    ];

    // --- Tab Handling ---
    window.switchTab = (tabId) => {
        document.querySelectorAll('.tab-content').forEach(el => el.classList.remove('active'));
        document.querySelectorAll('.tab-btn').forEach(el => el.classList.remove('active'));
        
        document.getElementById('tab-' + tabId).classList.add('active');
        const btns = document.querySelectorAll('.tab-btn');
        if(btns.length > 0) {
             if(tabId === 'overview') btns[0].classList.add('active');
             if(tabId === 'temporal') btns[1].classList.add('active');
             if(tabId === 'temporal') btns[1].classList.add('active');
             if(tabId === 'advanced') btns[2].classList.add('active');
             if(tabId === 'audit') btns[3].classList.add('active');
        }
        
        // Resize charts if needed
        if(tabId === 'overview') {
            if(charts.equity) charts.equity.resize();
            if(charts.periodic) charts.periodic.resize();
        }
    };
    
    // Periodic Chart Controls
    window.setPeriod = (p) => {
        currentPeriod = p;
        // Update Buttons UI
        document.querySelectorAll('.period-controls button').forEach(b => {
             if(b.textContent === p) b.classList.add('active');
             else if(['D','W','M','Y'].includes(b.textContent)) b.classList.remove('active');
        });
        applyFilters(); // Re-render logic handles the chart update
    };
    
    window.setChartType = (t) => {
        currentChartType = t;
        // Update Buttons UI
        document.querySelectorAll('.period-controls button').forEach(b => {
             const txt = b.textContent.toLowerCase();
             if(txt === t) b.classList.add('active');
             else if(['bar','line'].includes(txt)) b.classList.remove('active');
        });
         applyFilters(); // Re-render
    };

    const addFilesBtn = document.getElementById('add-files-btn');
    const clearDataBtn = document.getElementById('clear-data-btn');
    
    // Filters DOM Elements
    const filterInstrument = document.getElementById('filter-instrument');
    const filterSequence = document.getElementById('filter-target'); // Reusing variable name or new one? strict match to ID
    const filterTarget = document.getElementById('filter-target');
    const resetFiltersBtn = document.getElementById('reset-filters-btn');
    
    // --- Persistence Logic ---
    function saveData() {
        if(globalAllTrades.length > 0) {
            localStorage.setItem('rta_trades', JSON.stringify(globalAllTrades));
        }
    }

    // Removed enrichWithSequence as per user change of mind (Contract 1 vs 2)

    function loadData() {
        const stored = localStorage.getItem('rta_trades');
        if(stored) {
            try {
                const trades = JSON.parse(stored);
                // Fix Date objects
                trades.forEach(t => {
                    t.entryTime = new Date(t.entryTime);
                    if(t.exitTime) t.exitTime = new Date(t.exitTime);
                });
                
                if(trades.length > 0) {
                    globalAllTrades = trades;
                    populateFilters(globalAllTrades);
                    applyFilters();
                    dashboard.classList.remove('hidden');
                    dropZone.style.display = 'none';
                    if(addFilesBtn) addFilesBtn.style.display = 'inline-block';
                    if(clearDataBtn) clearDataBtn.style.display = 'inline-block';
                    console.log(`Loaded ${trades.length} trades from storage.`);
                }
            } catch(e) {
                console.error("Failed to load stored data", e);
            }
        }
    }

    function clearData() {
        if(confirm("Are you sure you want to clear all data?")) {
            localStorage.removeItem('rta_trades');
            globalAllTrades = [];
            location.reload(); // Simplest way to reset all charts/state
        }
    }

    if(clearDataBtn) clearDataBtn.addEventListener('click', clearData);
    
    // Load on start
    const storedRaw = localStorage.getItem('rta_trades');
    if(storedRaw) {
        // Show clear button if ANY data exists (even if load fails later)
        if(clearDataBtn) clearDataBtn.style.display = 'inline-block';
        loadData();
    }
    
    // --- Drag & Drop Handling ---
    dropZone.addEventListener('click', () => fileInput.click());
    if(addFilesBtn) addFilesBtn.addEventListener('click', () => fileInput.click());

    dropZone.addEventListener('dragover', (e) => {
        e.preventDefault();
        dropZone.classList.add('dragover');
    });

    dropZone.addEventListener('dragleave', () => {
        dropZone.classList.remove('dragover');
    });

    dropZone.addEventListener('drop', (e) => {
        e.preventDefault();
        dropZone.classList.remove('dragover');
        if (e.dataTransfer.files.length) {
            handleFiles(e.dataTransfer.files);
        }
    });

    fileInput.addEventListener('change', (e) => {
        if (e.target.files.length) {
            handleFiles(e.target.files);
        }
    });

    // --- Filter Logic ---
    function populateFilters(trades) {
         // Get unique instruments
         const instruments = [...new Set(trades.map(t => t.instrument))].sort();
         
         // Save current selection to restore if possible
         const currentVal = filterInstrument.value;
         
         // Clear (keep "All")
         filterInstrument.innerHTML = '<option value="all">All</option>';
         
         instruments.forEach(inst => {
             const option = document.createElement('option');
             option.value = inst;
             option.textContent = inst;
             filterInstrument.appendChild(option);
         });
         
         // Restore selection if it still exists
         if(instruments.includes(currentVal)) {
             filterInstrument.value = currentVal;
         }
    }

    function applyFilters() {
        const instVal = filterInstrument.value;
        const dirVal = filterDirection.value;
        const dayVal = filterDay.value; // 'all' or '0'-'6'
        const hourVal = filterHour.value; // 'all' or '0'-'23'
        const targetVal = filterTarget.value; // 'all', 'TP1', 'TP2', 'SL'

        const filtered = globalAllTrades.filter(t => {
            // Instrument
            if(instVal !== 'all' && t.instrument !== instVal) return false;
            
            // Direction (Check exact string match, adjust if case differs)
            if(dirVal !== 'all') {
                // Assuming t.type is "Long" or "Short"
                if(t.type !== dirVal) return false;
            }
            
            // Day
            if(dayVal !== 'all') {
                if(t.entryTime.getDay().toString() !== dayVal) return false;
            }
            
            // Hour
            if(hourVal !== 'all') {
                 if(t.entryTime.getHours().toString() !== hourVal) return false;
            }

            // Target Filter (New)
            if(targetVal !== 'all') {
                // Check if t.result (contains exit name) matches the target.
                // Assuming user CSV puts ExitName in the 'result' column (Index 7).
                // Or maybe the strategy logic ensures specific names.
                // Case insensitive check
                const res = (t.result || "").toUpperCase();
                
                if (targetVal === 'SL') {
                    if (!res.includes('SL')) return false; // Must indicate stop
                } 
                else if (targetVal === 'TP1') {
                    if (!res.includes('TP1')) return false;
                }
                else if (targetVal === 'TP2') {
                    if (!res.includes('TP2')) return false;
                }
            }
            
            return true;
        });
        
        processData(filtered);
    }

    // Filter Event Listeners
    [filterInstrument, filterDirection, filterDay, filterHour, filterTarget].forEach(el => {
        el.addEventListener('change', applyFilters);
    });

    if(resetFiltersBtn) {
        resetFiltersBtn.addEventListener('click', () => {
             filterInstrument.value = 'all';
             filterDirection.value = 'all';
             filterDay.value = 'all';
             filterHour.value = 'all';
             filterTarget.value = 'all';
             applyFilters();
        });
    }

    function handleFiles(fileList) {
        const promises = Array.from(fileList).map(file => readFile(file));

        Promise.all(promises).then(results => {
            let newTrades = [];
            results.forEach(trades => {
                newTrades = newTrades.concat(trades);
            });
            
            // Incremental Add with Upsert Logic (Update if exists, Add if new)
            let addedCount = 0;
            let updatedCount = 0;
            
            // Map existing trades by Key for fast lookup
            const getTradeKey = (t) => `${t.id}_${t.instrument}_${t.entryTime.getTime()}_${t.entryPrice}_${t.pnl}`;
            const tradeMap = new Map();
            globalAllTrades.forEach((t, index) => tradeMap.set(getTradeKey(t), index));
            
            newTrades.forEach(t => {
                const key = getTradeKey(t);
                if(tradeMap.has(key)) {
                    // Exists? Update it! (In case user added columns like ExitName to CSV)
                    const idx = tradeMap.get(key);
                    globalAllTrades[idx] = t; 
                    updatedCount++;
                } else {
                    // New? Add it!
                    globalAllTrades.push(t);
                    tradeMap.set(key, globalAllTrades.length - 1);
                    addedCount++;
                }
            });

            if (globalAllTrades.length > 0) {
                 console.log(`Processed Import: ${addedCount} New, ${updatedCount} Updated.`);
                 saveData(); // Save to localStorage
                 populateFilters(globalAllTrades); // Update instrument list
                 applyFilters(); // Apply current filters (or default 'all') to render
                 dashboard.classList.remove('hidden');
                 dropZone.style.display = 'none';
                 if(addFilesBtn) addFilesBtn.style.display = 'inline-block';
                 if(clearDataBtn) clearDataBtn.style.display = 'inline-block';
                 
                 // Feedback if practically nothing changed but user tried to load
                 if (addedCount === 0 && updatedCount === 0) {
                     // Still nice to show success or nothing, instead of error.
                     console.log("Data refreshed. No changes detected.");
                 }
            } else {
                alert('No valid trades found in the uploaded CSV files.');
            }
        });
    }

    function readFile(file) {
        return new Promise((resolve) => {
            const reader = new FileReader();
            reader.onload = (e) => {
                const text = e.target.result;
                resolve(parseCSV(text));
            };
            reader.readAsText(file);
        });
    }

    // --- Parsing Logic ---
    function parseCSV(text) {
        const lines = text.trim().split('\n');
        if (lines.length < 2) return []; 

        const trades = [];
        // Detect Delimiter (Line 1 header usually has multiple)
        const header = lines[0];
        const delimiter = header.includes(';') ? ';' : ','; 
        console.log("Detected Delimiter:", delimiter);

        for (let i = 1; i < lines.length; i++) {
            const line = lines[i].trim();
            if (!line) continue;
            
            const cols = line.split(delimiter);
            
            // Relaxed check: Custom exports might have fewer columns. 
            if (cols.length < 5) { // Absolute minimum
                console.warn(`Row ${i} skipped: Not enough columns (${cols.length})`, line);
                continue; 
            }

            try {
                // Formatting Helper
                const parseNum = (str) => {
                    if(!str) return 0;
                    let clean = str.trim();
                    // Heuristic for Euro format 1.000,00
                    if(clean.indexOf(',') > -1 && clean.indexOf('.') > -1) {
                         if(clean.lastIndexOf(',') > clean.lastIndexOf('.')) { // 1.000,00
                             clean = clean.replace(/\./g, "").replace(',', '.');
                         } else { // 1,000.00
                             clean = clean.replace(/,/g, ""); 
                         }
                    } else if (clean.indexOf(',') > -1) {
                         clean = clean.replace(',', '.');
                    }
                    clean = clean.replace(/[^0-9.-]/g, "");
                    return parseFloat(clean);
                };

                const trade = {
                    id: cols[0],
                    instrument: cols[1],
                    entryTime: new Date(cols[2]), 
                    type: cols[3],
                    entryPrice: parseNum(cols[4]),
                    exitTime: cols[5] ? new Date(cols[5]) : null,
                    exitPrice: parseNum(cols[6]),
                    result: cols[7], // Exit Name
                    pnl: parseNum(cols[8]),
                    mae: cols[9] ? parseNum(cols[9]) : 0,
                    mfe: cols[10] ? parseNum(cols[10]) : 0
                };
                
                if (isNaN(trade.pnl)) {
                    continue;
                }

                trades.push(trade);
            } catch (err) {
                console.warn("Row parsing error", err);
            }
        }
        
        if (trades.length === 0) {
             alert(`Warning: Could not parse any trades.\n\nCheck:\n1. Is the CSV delimiter '${delimiter}' correct?\n2. Are dates/numbers in a valid format?\n3. Does the CSV have headers?\n\n(See Console F12 for details)`);
        }
        return trades;
    }

    // --- Processing & UI ---
    function processData(trades) {
        // Sort by Time (Global Execution Order)
        trades.sort((a, b) => a.entryTime - b.entryTime);

        // Identify Unique Instruments
        const instruments = [...new Set(trades.map(t => t.instrument))];
        
        // --- 1. Calculate General Stats (Total) ---
        let runningEquity = 0;
        let totalStats = createStatsObject();
        
        // --- 2. Calculate Stats Per Instrument ---
        let instrumentStats = {};
        instruments.forEach(inst => {
            instrumentStats[inst] = {
                runningEquity: 0,
                equityCurve: [{ x: 0, y: 0, date: null }], // Start at 0
                trades: []
            };
        });

        // Global Chart Data
        let globalEquityCurve = [{ x: 0, y: 0, date: null }];
        
        // Temporal Data
        const hourStats = {}; 
        const dayStats = {};  

        trades.forEach((trade, index) => {
            if (!isNaN(trade.pnl)) {
                
                // --- Global Accumulation ---
                runningEquity += trade.pnl;
                updateStats(totalStats, trade, runningEquity);

                globalEquityCurve.push({
                    x: index + 1,
                    y: runningEquity,
                    date: trade.exitTime || trade.entryTime
                });

                // --- Instrument Accumulation ---
                if (instrumentStats[trade.instrument]) {
                    const iStats = instrumentStats[trade.instrument];
                    iStats.runningEquity += trade.pnl;
                    iStats.trades.push(trade);
                    
                    iStats.equityCurve.push({
                        x: index + 1, // Use GLOBAL index to align with total equity curve
                        y: iStats.runningEquity,
                        date: trade.exitTime || trade.entryTime
                    });
                }

                // --- Temporal Stats (Global) ---
                const hour = trade.entryTime.getHours();
                const day = trade.entryTime.getDay(); // 0 = Sun

                if (!hourStats[hour]) hourStats[hour] = 0;
                hourStats[hour] += trade.pnl;

                if (!dayStats[day]) dayStats[day] = 0;
                dayStats[day] += trade.pnl;
            }
        });

        // Calculate Final Derived Stats (WinRate, PF, etc)
        deriveStats(totalStats, runningEquity);

        // Update DOM
        updateDOMStats(totalStats);

        // --- Render Charts ---
        renderMultiEquityChart('equityChart', globalEquityCurve, instrumentStats, instruments);
        updatePeriodicChart(trades); // <--- Added missing call
        renderBarChart('hourChart', hourStats, Object.keys(hourStats).sort((a,b)=>a-b), "Hour");
        renderDayChart('dayChart', dayStats);
        renderScatterChart('maeChart', trades, 'mae', 'pnl');
        renderScatterChart('mfeChart', trades, 'mfe', 'pnl');

        // --- Render Table ---
        renderTable([...trades].reverse());

        // --- Calculate Audit Stats ---
        calculateAuditStats(trades);
    }

    // --- Helpers ---
    function createStatsObject() {
        return {
            wins: 0, losses: 0,
            grossProfit: 0, grossLoss: 0,
            peakEquity: 0, maxDrawdown: 0,
            totalWinPnL: 0, totalLossPnL: 0,
            netProfit: 0, winRate: 0, profitFactor: 0, ev: 0, avgWin: 0, avgLoss: 0
        };
    }

    function updateStats(stats, trade, currentEquity) {
        if (trade.pnl > 0) {
            stats.wins++;
            stats.grossProfit += trade.pnl;
            stats.totalWinPnL += trade.pnl;
        } else if (trade.pnl < 0) {
            stats.losses++;
            stats.grossLoss += Math.abs(trade.pnl);
            stats.totalLossPnL += trade.pnl;
        }

        if (currentEquity > stats.peakEquity) stats.peakEquity = currentEquity;
        const dd = stats.peakEquity - currentEquity;
        if (dd > stats.maxDrawdown) stats.maxDrawdown = dd;
    }

    function deriveStats(stats, finalEquity) {
        const totalTrades = stats.wins + stats.losses;
        stats.winRate = totalTrades > 0 ? (stats.wins / totalTrades * 100).toFixed(1) : 0;
        stats.profitFactor = stats.grossLoss > 0 ? (stats.grossProfit / stats.grossLoss).toFixed(2) : "∞";
        stats.netProfit = finalEquity;
        stats.avgWin = stats.wins > 0 ? stats.totalWinPnL / stats.wins : 0;
        stats.avgLoss = stats.losses > 0 ? stats.totalLossPnL / stats.losses : 0;
        stats.ev = (stats.avgWin * (stats.wins/totalTrades)) + (stats.avgLoss * (stats.losses/totalTrades));
    }

    function updateDOMStats(stats) {
        document.getElementById('stat-net-profit').textContent = formatCurrency(stats.netProfit);
        document.getElementById('stat-net-profit').className = 'stat-value ' + (stats.netProfit >= 0 ? 'pnl-pos' : 'pnl-neg');
        document.getElementById('stat-win-rate').textContent = stats.winRate + '%';
        document.getElementById('stat-profit-factor').textContent = stats.profitFactor;
        document.getElementById('stat-ev').textContent = formatCurrency(stats.ev);
        document.getElementById('stat-avg-win-loss').textContent = `${formatCurrency(stats.avgWin)} / ${formatCurrency(stats.avgLoss)}`;
        document.getElementById('stat-max-dd').textContent = formatCurrency(stats.maxDrawdown);
    }

    function formatCurrency(val) {
        return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(val);
    }

    // --- Charting Logic ---

    function renderMultiEquityChart(id, globalData, instrumentStats, instruments) {
        const ctx = document.getElementById(id).getContext('2d');
        if (charts[id]) charts[id].destroy();

        // 1. Total Equity Dataset
        const datasets = [{
            label: 'Total Portfolio',
            data: globalData,
            borderColor: '#3b82f6', // Blueprint Blue
            backgroundColor: 'rgba(59, 130, 246, 0.1)',
            borderWidth: 3,
            fill: true,
            tension: 0.2,
            pointRadius: 0,
            order: 10 // Draw BEHIND (Higher order = background layer in this context usually, or keep consistent)
            // Actually, Chart.js "order": 0 is TOP. So Total should be high number to be bottom?
            // "The dataset with the lowest order is drawn last (on top)."
            // So default is 0. 
            // We want Instruments on Top -> Order 0.
            // Total on Bottom -> Order 1.
        }];
        datasets[0].order = 10; // Explicitly set Total to background

        // 2. Instrument Datasets
        
        // Get Max X from globalData
        const maxX = globalData.length > 0 ? globalData[globalData.length - 1].x : 0;

        instruments.forEach((inst, idx) => {
            const iData = [...instrumentStats[inst].equityCurve]; // Copy
            
            // Extend to End Logic
            // If the instrument stopped trading, extend a flat line to mark its final contribution relative to total time
            if (iData.length > 0 && maxX > 0) {
                const lastPt = iData[iData.length - 1];
                if (lastPt.x < maxX) {
                     // Add a point at the very end
                    iData.push({
                        x: maxX,
                        y: lastPt.y,
                        date: globalData[globalData.length - 1].date
                    });
                }
            }
            
            datasets.push({
                label: inst,
                data: iData,
                borderColor: COLORS[idx % COLORS.length],
                borderWidth: 2,
                borderDash: [5, 5],
                fill: false,
                tension: 0, // 0 for straight connect lines
                pointRadius: 0,
                order: 0 // Draw ON TOP (Lower order)
            });
        });

        charts[id] = new Chart(ctx, {
            type: 'line',
            data: { datasets: datasets },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                scales: {
                    x: { type: 'linear', title: { display: true, text: 'Trade #' }, grid: { color: '#334155' } },
                    y: { grid: { color: '#334155' } }
                },
                plugins: { 
                    legend: { 
                        display: true,
                        labels: { color: '#94a3b8', font: {family: 'Inter'} }
                    },
                    tooltip: {
                        mode: 'index',
                        intersect: false,
                        callbacks: {
                            label: function(context) {
                                let label = context.dataset.label || '';
                                if (label) {
                                    label += ': ';
                                }
                                if (context.parsed.y !== null) {
                                    label += new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(context.parsed.y);
                                }
                                return label;
                            }
                        }
                    }
                }
            }
        });
    }

    function renderBarChart(id, dataObj, labels, xLabel) {
        const ctx = document.getElementById(id).getContext('2d');
        if (charts[id]) charts[id].destroy();

        const dataValues = labels.map(l => dataObj[l] || 0);
        const bgColors = dataValues.map(v => v >= 0 ? '#10b981' : '#ef4444');

        charts[id] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    label: 'PnL',
                    data: dataValues,
                    backgroundColor: bgColors,
                    borderRadius: 4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    x: { grid: { display: false }, ticks: { color: '#94a3b8' } },
                    y: { grid: { color: '#334155' }, ticks: { color: '#94a3b8' } }
                },
                plugins: { legend: { display: false } }
            }
        });
    }

    function renderDayChart(id, dataObj) {
        const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
        const labels = [0, 1, 2, 3, 4, 5, 6];
        const ctx = document.getElementById(id).getContext('2d');
        if (charts[id]) charts[id].destroy();
        
        const dataValues = labels.map(l => dataObj[l] || 0);
        const bgColors = dataValues.map(v => v >= 0 ? '#10b981' : '#ef4444');

        charts[id] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: days,
                datasets: [{
                    label: 'PnL',
                    data: dataValues,
                    backgroundColor: bgColors,
                    borderRadius: 4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    x: { grid: { display: false }, ticks: { color: '#94a3b8' } },
                    y: { grid: { color: '#334155' }, ticks: { color: '#94a3b8' } }
                },
                plugins: { legend: { display: false } }
            }
        });
    }

    function renderScatterChart(id, trades, xProp, yProp) {
        const ctx = document.getElementById(id).getContext('2d');
        if (charts[id]) charts[id].destroy();

        const dataPoints = trades.map(t => ({
            x: t[xProp],
            y: t[yProp]
        })).filter(p => p.x !== 0);

        charts[id] = new Chart(ctx, {
            type: 'scatter',
            data: {
                datasets: [{
                    label: 'Trades',
                    data: dataPoints,
                    backgroundColor: dataPoints.map(p => p.y >= 0 ? 'rgba(16, 185, 129, 0.7)' : 'rgba(239, 68, 68, 0.7)')
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    x: { 
                        type: 'linear', 
                        position: 'bottom', 
                        title: { display: true, text: xProp.toUpperCase(), color: '#94a3b8' },
                        grid: { color: '#334155' }
                    },
                    y: { 
                        title: { display: true, text: 'PnL', color: '#94a3b8' },
                        grid: { color: '#334155' }
                    }
                },
                 plugins: { legend: { display: false } }
            }
        });
    }

    function updatePeriodicChart(trades) {
        const ctx = document.getElementById('periodicChart').getContext('2d');
        if (charts.periodic) charts.periodic.destroy();

        // 1. Aggregate Data
        const aggregated = {}; // Key -> { pnl, count, win }
        
        trades.forEach(t => {
            let key;
            const d = t.entryTime;
            
            if (currentPeriod === 'D') {
                key = d.toISOString().split('T')[0]; // YYYY-MM-DD
            } else if (currentPeriod === 'M') {
                 key = `${d.getFullYear()}-${(d.getMonth()+1).toString().padStart(2,'0')}`; // YYYY-MM
            } else if (currentPeriod === 'Y') {
                 key = `${d.getFullYear()}`; // YYYY
            } else if (currentPeriod === 'W') {
                 // Get Monday of the week
                 const day = d.getDay(),
                 diff = d.getDate() - day + (day == 0 ? -6 : 1); // adjust when day is sunday
                 const monday = new Date(d);
                 monday.setDate(diff);
                 key = monday.toISOString().split('T')[0];
            }
            
            if (!aggregated[key]) aggregated[key] = { pnl: 0, count: 0 };
            aggregated[key].pnl += t.pnl;
            aggregated[key].count++;
        });
        
        // Sort keys
        const labels = Object.keys(aggregated).sort();
        const data = labels.map(k => aggregated[k].pnl);
        
        // Colors
        const bgColors = data.map(v => v >= 0 ? 'rgba(16, 185, 129, 0.6)' : 'rgba(239, 68, 68, 0.6)');
        const borderColors = data.map(v => v >= 0 ? '#10b981' : '#ef4444');

        // Config
        const dataset = {
            label: 'Net PnL',
            data: data,
            backgroundColor: bgColors,
            borderColor: borderColors,
            borderWidth: 1,
        };
        
        if(currentChartType === 'line') {
             // For line chart, maybe show cumulative? Or just connected points of periodic PnL? 
             // "Performance History" usually implies discrete bars per period. 
             // If user requested "Line", literally change type to line.
             dataset.type = 'line';
             dataset.fill = false;
             dataset.tension = 0.3;
             dataset.borderColor = '#6366f1'; // Unified color for line
             dataset.pointBackgroundColor = bgColors;
             // If line, remove background color array (or keep for points)
        }

        charts.periodic = new Chart(ctx, {
            type: currentChartType === 'line' ? 'line' : 'bar',
            data: {
                labels: labels,
                datasets: [dataset]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                         callbacks: {
                              label: function(context) {
                                   return formatCurrency(context.raw);
                              }
                         }
                    }
                },
                scales: {
                    y: {
                        beginAtZero: true,
                        grid: { color: 'rgba(255, 255, 255, 0.1)' },
                        ticks: { color: '#94a3b8' }
                    },
                    x: {
                        grid: { display: false },
                        ticks: { color: '#94a3b8' }
                    }
                }
            }
        });
    }

    function renderTable(trades) {
        const tbody = document.getElementById('trade-table-body');
        tbody.innerHTML = '';
        trades.forEach(t => {
            const tr = document.createElement('tr');
            const pnlClass = t.pnl > 0 ? 'pnl-pos' : (t.pnl < 0 ? 'pnl-neg' : '');
            
            tr.innerHTML = `
                <td>${t.id}</td>
                <td>${t.entryTime.toLocaleString()}</td>
                <td>${t.exitTime ? t.exitTime.toLocaleString() : '-'}</td>
                <td>${t.type}</td>
                <td>${t.instrument || 'N/A'}</td>
                <td>${t.entryPrice.toFixed(2)}</td>
                <td>${t.exitTime ? t.exitPrice.toFixed(2) : '-'}</td>
                <td>${t.result}</td>
                <td class="${pnlClass}">${formatCurrency(t.pnl)}</td>
            `;
            tbody.appendChild(tr);
        });
    }

    // --- AUDIT & MATH LOGIC ---
    
    const MathUtils = {
        mean: (arr) => arr.length ? arr.reduce((a,b)=>a+b,0) / arr.length : 0,
        stdDev: (arr) => {
            if(arr.length < 2) return 0;
            const m = MathUtils.mean(arr);
            const variance = arr.reduce((sq, n) => sq + Math.pow(n - m, 2), 0) / (arr.length - 1);
            return Math.sqrt(variance);
        },
        tTestInfo: (arr) => {
            // One-sample t-test against mu=0
            const n = arr.length;
            if(n < 2) return { t: 0, p: 1, significant: false };
            const m = MathUtils.mean(arr);
            const s = MathUtils.stdDev(arr);
            const se = s / Math.sqrt(n);
            const t = m / se;
            // Approx p-value for large N (using normal distribution approximation)
            // This is a rough estimation sufficient for trading purposes
            // Alpha 0.05 => T > 1.96
            const significant = Math.abs(t) > 1.96;
            return { t: t.toFixed(2), significant: significant };
        },
        shuffle: (array) => {
            let currentIndex = array.length, randomIndex;
            while (currentIndex != 0) {
                randomIndex = Math.floor(Math.random() * currentIndex);
                currentIndex--;
                [array[currentIndex], array[randomIndex]] = [array[randomIndex], array[currentIndex]];
            }
            return array;
        }
    };

    function calculateAuditStats(trades) {
        if(!trades || trades.length === 0) return;

        // 1. Prepare Data
        const pnls = trades.map(t => t.pnl);
        const maes = trades.map(t => t.mae || 0);
        const mfes = trades.map(t => t.mfe || 0);

        // 2. Risk Metrics
        const avgMAE = MathUtils.mean(maes.filter(m => m > 0)); // Filter zeros if needed? Usually MAE is >= 0
        const avgMFE = MathUtils.mean(mfes.filter(m => m > 0));
        
        let totalMFE = mfes.reduce((a,b)=>a+b,0);
        const netProfit = pnls.reduce((a,b)=>a+b,0);
        const efficiency = totalMFE > 0 ? (netProfit / totalMFE) * 100 : 0;

        // 3. T-Test
        const tResult = MathUtils.tTestInfo(pnls);
        
        // 4. Update UI Immediate
        document.getElementById('audit-mae').textContent = avgMAE ? "$" + avgMAE.toFixed(2) : "--";
        document.getElementById('audit-mfe').textContent = avgMFE ? "$" + avgMFE.toFixed(2) : "--";
        document.getElementById('audit-efficiency').textContent = efficiency.toFixed(1) + "%";
        
        const tEl = document.getElementById('audit-ttest');
        const tDesc = document.getElementById('audit-ttest-desc');
        tEl.textContent = `T-Score: ${tResult.t}`;
        if(tResult.significant) {
            tEl.classList.add('positive-val');
            tDesc.textContent = "Statistically Significant (95%)";
        } else {
            tDesc.textContent = "Not Significant (Could be random)";
        }

        // 5. Monte Carlo (Async)
        document.getElementById('audit-montecarlo-desc').textContent = "Running 1000 simulations...";
        setTimeout(() => runMonteCarlo(pnls, netProfit), 100);
    }

    function runMonteCarlo(pnls, actualProfit) {
        const ITERATIONS = 1000;
        let worseCount = 0;
        
        // Use a copy to shuffle
        let baseArr = [...pnls];

        for(let i=0; i<ITERATIONS; i++) {
            MathUtils.shuffle(baseArr);
            // Calculate a metric for this random run (e.g., Total Profit, or Max DD)
            // Here we compare Total Profit. 
            // NOTE: Total Profit is mathematically INVARIANT to shuffling if position size is constant.
            // BUT: Drawdown IS NOT. 
            // So for "Luck" in Profit, we need to compare against a random entry strategy, NOT shuffling existing PnLs.
            // Shuffling existing PnLs checks for "Sequence Luck" (Ordering).
            // To check if the *Strategy* has edge, we usually test mean > 0 (T-Test).
            // Shuffling PnL is good for MaxDD luck.
            
            // Let's implement "Sequence Luck" check on Drawdown instead?
            // "Is my current Low Drawdown result of luck?"
            
            // OR: Bootstrap Resampling (Allow duplicates). 
            // If we resample with replacement, the sum changes.
            
            // Let's do Resampling (Bootstrap)
            let randomProfit = 0;
            for(let j=0; j<baseArr.length; j++) {
                 randomProfit += baseArr[Math.floor(Math.random() * baseArr.length)];
            }
            
            if(randomProfit < actualProfit) worseCount++;
        }
        
        const percentile = (worseCount / ITERATIONS) * 100;
        const div = document.getElementById('audit-montecarlo');
        div.textContent = `Better than ${percentile.toFixed(1)}% of random scenarios`;
        
        const desc = document.getElementById('audit-montecarlo-desc');
        if(percentile > 95) desc.textContent = "High Confidence of Edge";
        else if(percentile > 50) desc.textContent = "Average Edge";
        else desc.textContent = "Result likely random or lucky";
    }

}); // End of DOMContentLoaded
