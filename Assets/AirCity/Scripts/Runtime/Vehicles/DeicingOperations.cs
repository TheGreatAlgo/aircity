using System;
using UnityEngine;

namespace AirCity.Vehicles
{
    public enum DeicingExpense { Capital, Fluid, Payroll, Running }

    /// <summary>Serialized sandbox account. Expenses always accrue; a finite budget also gates purchases.</summary>
    [Serializable]
    public sealed class DeicingAccounts
    {
        public const double StartingFunds = 500000d;
        public bool limitedBudget;
        public double funds = StartingFunds, capitalSpent, fluidSpent, payrollSpent, runningSpent;
        public double addedFunding;
        public double appliedLitres, recoveredLitres;
        public double TotalSpent => capitalSpent + fluidSpent + payrollSpent + runningSpent;
        [NonSerialized] public Func<double> sharedBalance;
        [NonSerialized] public Func<bool> sharedFreeSpending;
        [NonSerialized] public Action<double> sharedExpense;
        public double AvailableFunds => sharedBalance != null ? sharedBalance() : funds;
        public bool FreeSpending => sharedFreeSpending != null ? sharedFreeSpending() : !limitedBudget;
        public bool CanAfford(double price) => FreeSpending || AvailableFunds + .000001d >= price;
        public bool AddFunding(double amount)
        {
            if (sharedBalance != null) return false;
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0d) return false;
            funds += amount; addedFunding += amount; return true;
        }
        internal void Record(double amount, DeicingExpense expense)
        {
            if (sharedExpense != null) sharedExpense(amount); else funds -= amount;
            switch (expense)
            {
                case DeicingExpense.Capital: capitalSpent += amount; break;
                case DeicingExpense.Fluid: fluidSpent += amount; break;
                case DeicingExpense.Payroll: payrollSpent += amount; break;
                default: runningSpent += amount; break;
            }
        }
    }

    public sealed partial class AirportDeicing
    {
        // Initial gameplay balance, expressed in dollars and game hours rather than real-world quotations.
        public const double TruckPrice = 180000d, SupplyAreaPrice = 35000d, RecyclingPrice = 45000d;
        public const double FluidPricePerLitre = 2d, DeliveryFeePerLoad = 150d;
        public const double OperatorHourlyWage = 24d, TruckHourlyUpkeep = 10d, PadHourlyPower = 6d;
        public const double DrivingHourlyCost = 18d, SprayingHourlyCost = 60d, RecyclingCostPerLitre = .08d;
        public const float RecoveryFraction = .6f, RecyclingDelaySeconds = 30f, RecyclingLitresPerSecond = 60f, RunoffCapacityLitres = 15000f;

        public DeicingAccounts Accounts => airport.deicingAccounts ??= new DeicingAccounts();
        public string LastPurchaseProblem { get; private set; }
        public static double DeliveryPrice(float litres) => litres * FluidPricePerLitre +
            Math.Ceiling(litres / DeliveryCapacityLitres) * DeliveryFeePerLoad;
        public float OrderCapacity(DeicingWorkArea area) => area == null ? 0f :
            Mathf.Max(0f, area.capacityLitres - area.fluidLitres - area.deliveryLitres - area.runoffLitres);
        bool Purchase(double price, DeicingExpense expense, out string problem)
        {
            problem = double.IsNaN(price) || double.IsInfinity(price) || price < 0d ? "invalid purchase price" :
                !Accounts.CanAfford(price) ? $"insufficient deicing funds: ${price:N0} required" : null;
            if (problem != null) return false;
            Accounts.Record(price, expense); return true;
        }
        public bool BuyRecycling(DeicingWorkArea area, out string problem)
        {
            problem = area == null || !airport.deicingWorkAreas.Contains(area) ? "choose a supply work area" :
                area.recyclingInstalled ? "recycling is already installed" : InUse(area) ? "wait for trucks to clear the work area" : null;
            if (problem != null || !Purchase(RecyclingPrice, DeicingExpense.Capital, out problem)) return false;
            area.recyclingInstalled = true; return true;
        }
        public int OperatorsFor(DeicingFacility facility) => facility != null && facility.crewOnDuty ? DeliveredTruckCount(facility) : 0;
        public double StandingHourlyCost(DeicingFacility facility) => OperatorsFor(facility) == 0 ? 0d :
            OperatorsFor(facility) * (OperatorHourlyWage + TruckHourlyUpkeep) + PadHourlyPower;
        public bool CanEndShift(DeicingFacility facility)
        {
            var layout = Layout(facility);
            if (layout == null || JobFor(facility) is DeicingJob job && !job.Completed) return false;
            foreach (var truck in TrucksFor(facility))
                if (!truck.DeliveryPending && (!truck.Drive.Finished || truck.Stage == DeicingTruckStage.Filling ||
                    Vector2.Distance(truck.Position, ParkingPose(truck).position) > 1f)) return false;
            return true;
        }
        public bool SetCrewOnDuty(DeicingFacility facility, bool onDuty, out string problem)
        {
            problem = Layout(facility) == null ? "choose a deicing pad" :
                !onDuty && !CanEndShift(facility) ? "wait for treatment and truck return before ending the shift" : null;
            if (problem != null) return false;
            facility.crewOnDuty = onDuty; return true;
        }
        void CollectRunoff(DeicingFacility facility, float sprayed)
        {
            if (sprayed <= 0f) return;
            Accounts.appliedLitres += sprayed;
            var area = WorkAreaFor(facility);
            if (area == null || !area.recyclingInstalled) return;
            float collected = Mathf.Min(sprayed * RecoveryFraction, Mathf.Max(0f, RunoffCapacityLitres - area.runoffLitres));
            if (collected <= 0f) return;
            if (area.runoffLitres <= .0001f) area.recyclingDelayRemaining = RecyclingDelaySeconds;
            area.runoffLitres += collected; area.collectedLitres += collected;
        }
        void TickRecycling(float seconds)
        {
            foreach (var area in WorkAreas)
            {
                if (area == null || !area.recyclingInstalled || area.runoffLitres <= 0f) continue;
                float processing = seconds;
                if (area.recyclingDelayRemaining > 0f)
                {
                    float waiting = Mathf.Min(processing, area.recyclingDelayRemaining);
                    area.recyclingDelayRemaining -= waiting; processing -= waiting;
                }
                // Supplier cargo keeps its reserved storage space. Excess runoff waits in the recovery buffer.
                float space = Mathf.Max(0f, area.capacityLitres - area.fluidLitres - area.deliveryLitres);
                float returned = Mathf.Min(area.runoffLitres, Mathf.Min(processing * RecyclingLitresPerSecond, space));
                if (returned <= 0f) continue;
                area.runoffLitres = Mathf.Max(0f, area.runoffLitres - returned); area.fluidLitres += returned;
                area.recycledLitres += returned; Accounts.recoveredLitres += returned;
                Accounts.Record(returned * RecyclingCostPerLitre, DeicingExpense.Running);
            }
        }
        void TickOperatingCosts(float seconds)
        {
            double hours = seconds / 3600d;
            foreach (var facility in Facilities)
            {
                if (facility == null || Layout(facility) == null) continue;
                int crew = OperatorsFor(facility);
                Accounts.Record(crew * OperatorHourlyWage * hours, DeicingExpense.Payroll);
                double running = crew == 0 ? 0d : crew * TruckHourlyUpkeep + PadHourlyPower;
                foreach (var truck in TrucksFor(facility))
                {
                    if (truck.DeliveryPending) continue;
                    if (Mathf.Abs(truck.Speed) > .001f) running += DrivingHourlyCost;
                    if (truck.IsSpraying) running += SprayingHourlyCost;
                }
                Accounts.Record(running * hours, DeicingExpense.Running);
            }
        }
    }
}
