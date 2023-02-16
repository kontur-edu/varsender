record Letter(
    string To,
    string SendTime,
    string Template
    )
{
    Dictionary<string, string> Variables = new Dictionary<string, string>();

    public Letter AddVariable(string key, string value)
    {
        Variables[key] = value;
        return this;
    }

    public Dictionary<string, string> GetVariables() => Variables.ToDictionary(it => it.Key, it => it.Value);
}
