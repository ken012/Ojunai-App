"use client"

import { Tabs as TabsPrimitive } from "@base-ui/react/tabs"
import { cva, type VariantProps } from "class-variance-authority"

import { cn } from "@/lib/utils"

function Tabs({
  className,
  orientation = "horizontal",
  ...props
}: TabsPrimitive.Root.Props) {
  return (
    <TabsPrimitive.Root
      data-slot="tabs"
      data-orientation={orientation}
      className={cn(
        // Default to vertical stacking (tabs on top, panel below) for horizontal orientation.
        // Base-ui sets data-orientation=horizontal, and we force flex-col so TabsList sits above content.
        "group/tabs flex flex-col gap-2",
        className
      )}
      {...props}
    />
  )
}

const tabsListVariants = cva(
  // Base: a rounded pill container that reads as a clickable button group.
  // Matches the dashboard's sidebar aesthetic (slate surfaces with sky-500 accents on active state).
  "group/tabs-list inline-flex w-fit items-center justify-center rounded-lg border border-slate-200 bg-slate-100 p-1 gap-1 text-slate-600",
  {
    variants: {
      variant: {
        default: "",
        // Line variant: transparent with an underline for the active tab (used when a softer look fits).
        line: "border-transparent bg-transparent gap-4 p-0 rounded-none",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

function TabsList({
  className,
  variant = "default",
  ...props
}: TabsPrimitive.List.Props & VariantProps<typeof tabsListVariants>) {
  return (
    <TabsPrimitive.List
      data-slot="tabs-list"
      data-variant={variant}
      className={cn(tabsListVariants({ variant }), className)}
      {...props}
    />
  )
}

function TabsTrigger({ className, ...props }: TabsPrimitive.Tab.Props) {
  return (
    <TabsPrimitive.Tab
      data-slot="tabs-trigger"
      className={cn(
        // Shared: button-like affordance with explicit cursor-pointer so the clickability is obvious.
        "relative cursor-pointer select-none rounded-md px-4 py-2 text-sm font-medium whitespace-nowrap transition-all outline-none",
        "focus-visible:ring-2 focus-visible:ring-sky-400/50 focus-visible:ring-offset-1",
        "disabled:pointer-events-none disabled:opacity-50 aria-disabled:pointer-events-none aria-disabled:opacity-50",
        "[&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
        // Default variant (pill group): hover goes white, active goes sky-500 with shadow lift.
        "group-data-[variant=default]/tabs-list:text-slate-600",
        "group-data-[variant=default]/tabs-list:hover:bg-white",
        "group-data-[variant=default]/tabs-list:hover:text-slate-900",
        "group-data-[variant=default]/tabs-list:data-active:bg-sky-500",
        "group-data-[variant=default]/tabs-list:data-active:text-white",
        "group-data-[variant=default]/tabs-list:data-active:shadow-sm",
        "group-data-[variant=default]/tabs-list:data-active:font-semibold",
        // Line variant: underlined active tab, no background.
        "group-data-[variant=line]/tabs-list:rounded-none",
        "group-data-[variant=line]/tabs-list:px-1",
        "group-data-[variant=line]/tabs-list:pb-2",
        "group-data-[variant=line]/tabs-list:text-slate-500",
        "group-data-[variant=line]/tabs-list:hover:text-slate-900",
        "group-data-[variant=line]/tabs-list:data-active:text-sky-600",
        "group-data-[variant=line]/tabs-list:data-active:font-semibold",
        "group-data-[variant=line]/tabs-list:after:absolute",
        "group-data-[variant=line]/tabs-list:after:inset-x-0",
        "group-data-[variant=line]/tabs-list:after:bottom-0",
        "group-data-[variant=line]/tabs-list:after:h-0.5",
        "group-data-[variant=line]/tabs-list:after:bg-sky-500",
        "group-data-[variant=line]/tabs-list:after:opacity-0",
        "group-data-[variant=line]/tabs-list:after:transition-opacity",
        "group-data-[variant=line]/tabs-list:data-active:after:opacity-100",
        className
      )}
      {...props}
    />
  )
}

function TabsContent({ className, ...props }: TabsPrimitive.Panel.Props) {
  return (
    <TabsPrimitive.Panel
      data-slot="tabs-content"
      className={cn("text-sm outline-none", className)}
      {...props}
    />
  )
}

export { Tabs, TabsList, TabsTrigger, TabsContent, tabsListVariants }
