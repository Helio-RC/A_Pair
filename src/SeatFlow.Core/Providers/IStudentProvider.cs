namespace SeatFlow.Core.Providers
{
    /// <summary>
    /// 学生数据提供器接口，定义从不同数据源加载学生列表的契约。
    /// 实现类包括 CSV、Excel (XLSX)、JSON 和内存数据源。
    /// </summary>
    public interface IStudentProvider
    {
        /// <summary>
        /// 从指定数据源加载学生列表。
        /// </summary>
        /// <param name="source">数据源路径或连接字符串，具体格式由实现类决定。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>学生列表。</returns>
        Task<List<Models.Student>> LoadAsync (string source , CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取数据源文件的维度（行数 × 列数），用于导入前的范围判断。
        /// 默认实现返回 (0, 0) 表示未知维度。
        /// </summary>
        /// <param name="source">数据源路径。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>(行数, 列数)。(0, 0) 表示无法确定维度。</returns>
        Task<(int Rows , int Cols)> GetDimensionsAsync (string source , CancellationToken ct = default)
        {
            return Task.FromResult((0 , 0));
        }

        /// <summary>
        /// 从指定数据源加载学生列表，可选地限制扫描的行数和列数。
        /// 默认实现忽略限制，委托给 <see cref="LoadAsync(string, CancellationToken)"/>。
        /// </summary>
        /// <param name="source">数据源路径。</param>
        /// <param name="maxRows">最大扫描行数。0 或负数表示不限制。</param>
        /// <param name="maxCols">最大扫描列数。0 或负数表示不限制。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>学生列表。</returns>
        Task<List<Models.Student>> LoadAsync (string source , int maxRows , int maxCols , CancellationToken ct = default)
        {
            return LoadAsync(source , ct);
        }
    }
}
