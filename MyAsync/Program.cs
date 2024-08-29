using MyAsync;

Console.WriteLine("");

await PrintAsync();

static async MyTask PrintAsync()
{
    for (int i = 0; i < 10; i++)
    {
        await MyTask.Delay(1000);
        Console.WriteLine(i);
    }
}
