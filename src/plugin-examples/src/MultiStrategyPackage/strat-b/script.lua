-- 策略 B：倒序分配（演示同包多策略）
local unassigned = workspace:GetUnassignedStudentIds()
local empty = workspace:GetEmptySeatIds()

table.sort(unassigned, function(a, b) return a > b end)
for i = 1, math.min(#unassigned, #empty) do
    workspace:AssignSeat(empty[i], unassigned[i])
end
