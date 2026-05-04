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
  // Matches the dashboard's sidebar aesthetic (slate surfaces with cyan-500 accents on active state).
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
        // Base: button-like with cursor pointer. Uses aria-selected for active state because
        // base-ui's Tab sets aria-selected="true" on the active tab — more reliable than data-active.
        "relative cursor-pointer select-none rounded-md px-4 py-2 text-sm font-medium whitespace-nowrap transition-all outline-none",
        "text-slate-600 hover:bg-white hover:text-slate-900",
        "focus-visible:ring-2 focus-visible:ring-cyan-400/50 focus-visible:ring-offset-1",
        "disabled:pointer-events-none disabled:opacity-50",
        "[&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
        // Active state — cyan-500 blue to match the contact filter buttons
        "aria-selected:bg-cyan-500 aria-selected:text-white aria-selected:shadow-sm aria-selected:font-semibold",
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
