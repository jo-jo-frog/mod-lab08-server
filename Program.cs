using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Globalization;

namespace Lab08_MyCMO
{
    public class ClientRequestEventArgs : EventArgs
    {
        public int Id { get; set; }
    }

    public class ServiceServer
    {
        private int channels;
        private double serviceRate;
        private Random rand = new Random();

        private bool[] busy;
        private Thread[] threads;

        private int totalArrived;
        private int processed;
        private int denied;

        public int TotalArrived => totalArrived;
        public int Processed => processed;
        public int Denied => denied;

        private int idleChecks;
        private int idleDetected;
        private object statLock = new object();
        private bool monitoring = true;

        public ServiceServer(int n, double mu)
        {
            channels = n;
            serviceRate = mu;
            busy = new bool[n];
            threads = new Thread[n];

            Thread monitor = new Thread(MonitorIdle);
            monitor.IsBackground = true;
            monitor.Start();
        }

        private void MonitorIdle()
        {
            while (monitoring)
            {
                Thread.Sleep(100);
                bool allFree = true;
                lock (busy)
                {
                    for (int i = 0; i < channels; i++)
                        if (busy[i]) { allFree = false; break; }
                }
                lock (statLock)
                {
                    idleChecks++;
                    if (allFree) idleDetected++;
                }
            }
        }

        private void ServeRequest(object arg)
        {
            double serviceTime = -Math.Log(1.0 - rand.NextDouble()) / serviceRate;
            Thread.Sleep((int)(serviceTime * 1000));

            lock (busy)
            {
                for (int i = 0; i < channels; i++)
                    if (threads[i] == Thread.CurrentThread)
                    {
                        busy[i] = false;
                        threads[i] = null;
                        break;
                    }
            }
        }

        public void HandleRequest(object sender, ClientRequestEventArgs e)
        {
            totalArrived++;

            lock (busy)
            {
                for (int i = 0; i < channels; i++)
                {
                    if (!busy[i])
                    {
                        busy[i] = true;
                        threads[i] = new Thread(ServeRequest);
                        threads[i].Start(e.Id);
                        processed++;
                        return;
                    }
                }
                denied++;
            }
        }

        public void Stop()
        {
            monitoring = false;
            bool anyBusy;
            do
            {
                anyBusy = false;
                lock (busy)
                {
                    for (int i = 0; i < channels; i++)
                        if (busy[i]) { anyBusy = true; break; }
                }
                if (anyBusy) Thread.Sleep(20);
            } while (anyBusy);
        }

        public double GetIdleProbability()
        {
            lock (statLock)
            {
                if (idleChecks == 0) return 0;
                return (double)idleDetected / idleChecks;
            }
        }
    }

    public class Requester
    {
        public event EventHandler<ClientRequestEventArgs> RequestArrived;

        public Requester(ServiceServer server)
        {
            this.RequestArrived += server.HandleRequest;
        }

        public void GenerateRequest(int id)
        {
            RequestArrived?.Invoke(this, new ClientRequestEventArgs { Id = id });
        }
    }

    public class ExperimentResult
    {
        public double Lambda { get; set; }
        public double ExpP0 { get; set; }
        public double TheorP0 { get; set; }
        public double ExpPout { get; set; }
        public double TheorPout { get; set; }
        public double ExpQ { get; set; }
        public double TheorQ { get; set; }
        public double ExpA { get; set; }
        public double TheorA { get; set; }
        public double ExpAvgBusy { get; set; }
        public double TheorAvgBusy { get; set; }
        public int Total { get; set; }
        public int Processed { get; set; }
        public int Denied { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("Лабораторная работа №8 – Многоканальная СМО с отказами");

            const int n = 5;
            const double mu = 10.0;
            const double duration = 60.0;

            double[] lambdas = { 2, 4, 6, 8, 10, 12, 14, 16, 18, 20 };
            List<ExperimentResult> results = new List<ExperimentResult>();

            foreach (double lambda in lambdas)
            {
                Console.WriteLine($"\n--- Эксперимент: λ = {lambda} заявок/сек ---");
                ExperimentResult data = RunExperiment(n, mu, lambda, duration);
                results.Add(data);
                PrintResult(data);
            }

            SaveResultsToCSV(results, n, mu);
            SaveResultsToTxt(results, n, mu);

            Console.WriteLine("\nГотово! Результаты сохранены в папке 'result'.");
            Console.WriteLine("Откройте result/results.csv в Excel и постройте графики.");
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        static ExperimentResult RunExperiment(int n, double mu, double lambda, double durationSec)
        {
            ServiceServer server = new ServiceServer(n, mu);
            Requester client = new Requester(server);
            Random rand = new Random();

            DateTime startTime = DateTime.Now;
            DateTime endTime = startTime.AddSeconds(durationSec);
            int requestId = 0;

            while (DateTime.Now < endTime)
            {
                double interval = -Math.Log(1.0 - rand.NextDouble()) / lambda;
                int ms = (int)(interval * 1000);
                if (ms > 0) Thread.Sleep(ms);
                if (DateTime.Now >= endTime) break;
                client.GenerateRequest(++requestId);
            }

            server.Stop();
            double realTime = (DateTime.Now - startTime).TotalSeconds;

            double expP0 = server.GetIdleProbability();
            double expPout = (double)server.Denied / server.TotalArrived;
            double expQ = (double)server.Processed / server.TotalArrived;
            double expA = server.Processed / realTime;
            double expAvgBusy = expA / mu;

            double rho = lambda / mu;
            double sum = 0;
            double fact = 1;
            for (int i = 0; i <= n; i++)
            {
                if (i > 0) fact *= i;
                sum += Math.Pow(rho, i) / fact;
            }
            double theorP0 = 1.0 / sum;
            double theorPout = (Math.Pow(rho, n) / fact) * theorP0;
            double theorQ = 1 - theorPout;
            double theorA = lambda * theorQ;
            double theorAvgBusy = rho * theorQ;

            return new ExperimentResult
            {
                Lambda = lambda,
                ExpP0 = expP0,
                TheorP0 = theorP0,
                ExpPout = expPout,
                TheorPout = theorPout,
                ExpQ = expQ,
                TheorQ = theorQ,
                ExpA = expA,
                TheorA = theorA,
                ExpAvgBusy = expAvgBusy,
                TheorAvgBusy = theorAvgBusy,
                Total = server.TotalArrived,
                Processed = server.Processed,
                Denied = server.Denied
            };
        }

        static void PrintResult(ExperimentResult d)
        {
            Console.WriteLine($"Заявок: всего = {d.Total}, обслужено = {d.Processed}, отказано = {d.Denied}");
            Console.WriteLine($"P0 (эксп/теор): {d.ExpP0:F4} / {d.TheorP0:F4}");
            Console.WriteLine($"Pотк (эксп/теор): {d.ExpPout:F4} / {d.TheorPout:F4}");
            Console.WriteLine($"Q (эксп/теор): {d.ExpQ:F4} / {d.TheorQ:F4}");
            Console.WriteLine($"A (эксп/теор): {d.ExpA:F2} / {d.TheorA:F2} заявок/сек");
            Console.WriteLine($"Ср.зан.каналов (эксп/теор): {d.ExpAvgBusy:F3} / {d.TheorAvgBusy:F3}");
        }

        static void SaveResultsToCSV(List<ExperimentResult> results, int n, double mu)
        {
            Directory.CreateDirectory("result");
            using (StreamWriter csv = new StreamWriter(Path.Combine("result", "results.csv")))
            {
                csv.WriteLine("Lambda,ExpP0,TheorP0,ExpPout,TheorPout,ExpQ,TheorQ,ExpA,TheorA,ExpAvgBusy,TheorAvgBusy");
                var inv = CultureInfo.InvariantCulture;
                foreach (var d in results)
                {
                    csv.WriteLine($"{d.Lambda.ToString(inv)},{d.ExpP0.ToString(inv)},{d.TheorP0.ToString(inv)}," +
                                  $"{d.ExpPout.ToString(inv)},{d.TheorPout.ToString(inv)},{d.ExpQ.ToString(inv)}," +
                                  $"{d.TheorQ.ToString(inv)},{d.ExpA.ToString(inv)},{d.TheorA.ToString(inv)}," +
                                  $"{d.ExpAvgBusy.ToString(inv)},{d.TheorAvgBusy.ToString(inv)}");
                }
            }
        }

        static void SaveResultsToTxt(List<ExperimentResult> results, int n, double mu)
        {
            Directory.CreateDirectory("result");
            using (StreamWriter w = new StreamWriter(Path.Combine("result", "results.txt")))
            {
                w.WriteLine("Лабораторная работа №8 – Многоканальная СМО с отказами");
                w.WriteLine($"Параметры: число каналов n = {n}, μ = {mu} заявок/сек\n");
                w.WriteLine("Таблица сравнения экспериментальных и теоретических показателей:\n");
                w.WriteLine("λ\tP0(эксп)\tP0(теор)\tPотк(эксп)\tPотк(теор)\tQ(эксп)\tQ(теор)\tA(эксп)\tA(теор)\tср.зан(эксп)\tср.зан(теор)");
                foreach (var d in results)
                {
                    w.WriteLine($"{d.Lambda:F2}\t{d.ExpP0:F4}\t{d.TheorP0:F4}\t{d.ExpPout:F4}\t{d.TheorPout:F4}\t" +
                                $"{d.ExpQ:F4}\t{d.TheorQ:F4}\t{d.ExpA:F2}\t{d.TheorA:F2}\t{d.ExpAvgBusy:F3}\t{d.TheorAvgBusy:F3}");
                }
                w.WriteLine("\nДополнительная статистика:");
                w.WriteLine("λ\tВсего заявок\tОбслужено\tОтказано");
                foreach (var d in results)
                    w.WriteLine($"{d.Lambda:F2}\t{d.Total}\t{d.Processed}\t{d.Denied}");
            }
        }
    }
}
