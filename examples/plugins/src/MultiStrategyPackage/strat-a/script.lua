-- 策略 A：固定第一排优先（演示同包多策略）
local unassigned = workspace:GetUnassignedStudentIds()
local empty = workspace:GetEmptySeatIds()

table.sort(unassigned)
for i = 1, math.min(#unassigned, #empty) do
    workspace:AssignSeat(empty[i], unassigned[i])
end
