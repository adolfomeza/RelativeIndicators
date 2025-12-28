# Complete v1.9.0 Implementation
# Execute este script para completar las modificaciones restantes

Write-Host "=== Completing v1.9.0: Adaptive Entry Orders ===" -ForegroundColor Cyan

$file = "SessionLevelsStrategy.cs"
$content = Get-Content $file -Raw -Encoding UTF8

# 1. Add tracking initialization after SHORT confirmation (~line 1799)
$pattern1 = '(\t\t\t\t\t\t\t\tentryOrder = SubmitOrderUnmanaged\(0, OrderAction\.SellShort.*?EntryA_Short"\);\r\n\t\t\t\t\t\t\t\tcurrentEntryState = EntryState\.workingOrder;)\r\n(\t\t\t\t\t\t\t\tLog\(Time\[0\] \+ " Order Submitted \(Short Consolidated\))'
$replacement1 = '$1

				// ADAPTIVE TRACKING (v1.9.0): Initialize
				lastUpdateVwap = limitPrice;
				lastUpdateQuantity = dynamicQuantity;

$2'
$content = $content -replace $pattern1, $replacement1

# 2. Add tracking initialization after LONG confirmation (~line 1883)
$pattern2 = '(\t\t\tentryOrder = SubmitOrderUnmanaged\(0, OrderAction\.Buy.*?EntryA_Long"\);\r\n\t\t\t\t\t\t\t\tcurrentEntryState = EntryState\.workingOrder;)\r\n(\t\t\t\t\t\t\t\tLog\(Time\[0\] \+ " Order Submitted \(Long Consolidated\))'
$replacement2 = '$1

				// ADAPTIVE TRACKING (v1.9.0): Initialize
				lastUpdateVwap = limitPrice;
				lastUpdateQuantity = dynamicQuantity;

$2'
$content = $content -replace $pattern2, $replacement2

# 3. Add workingOrder integration (call UpdateAdaptiveEntry before R/R validation)
# Find the workingOrder block and add the call
$pattern3 = '(if \(currentEntryState == EntryState\.workingOrder\)\r\n\s+\{)'
$replacement3 = '$1
			// ADAPTIVE UPDATE (v1.9.0): Update order price/quantity dynamically
			string direction = setupLevelName.Contains("High") ? "Short" : "Long";
			UpdateAdaptiveEntry(direction);
			'
$content = $content -replace $pattern3, $replacement3

# 4. Add resets in various places
# Reset in order cancellation/rejection
$pattern4 = '(cachedOppositeLevel = null;)'
$replacement4 = '$1
		lastUpdateVwap = 0;
		lastUpdateQuantity = 0;'
$content = $content -replace $pattern4, $replacement4

# Save
$content | Set-Content $file -Encoding UTF8 -NoNewline
Write-Host "✓ All modifications completed" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Compile in NinjaTrader (F5)"
Write-Host "2. Review compilation errors (if any)"
Write-Host "3. Test in Playback with EnableDebugLogs = true"
