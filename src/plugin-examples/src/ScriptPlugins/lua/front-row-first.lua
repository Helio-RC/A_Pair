-- 前排优先脚本策略（Lua）
-- 依赖策略仅通过受限 workspace API 操作：GetUnassignedStudentIds / GetEmptySeatIds / GetStudent / AssignSeat
-- 注意：NLua 中实例方法使用冒号语法（workspace:Method()）

local unassigned = workspace:GetUnassignedStudentIds()
local empty = workspace:GetEmptySeatIds()

-- 需要前排的学生优先，其次按姓名排序
table.sort(unassigned, function(a, b)
    local sa = workspace:GetStudent(a)
    local sb = workspace:GetStudent(b)
    if sa and sb then
        if sa.NeedsFrontRow ~= sb.NeedsFrontRow then
            return sa.NeedsFrontRow
        end
    end
    return a < b
end)

local count = math.min(#unassigned, #empty)
for i = 1, count do
    workspace:AssignSeat(empty[i], unassigned[i])
end
