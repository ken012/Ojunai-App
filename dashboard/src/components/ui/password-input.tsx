"use client";

import * as React from "react";
import { Eye, EyeOff } from "lucide-react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

/**
 * Password field with a show/hide toggle so users can confirm what they typed.
 * Drop-in for <Input type="password" …> — forwards the ref (so react-hook-form
 * `register()` works) and passes through all input props.
 */
const PasswordInput = React.forwardRef<HTMLInputElement, React.ComponentProps<"input">>(
  ({ className, ...props }, ref) => {
    const [show, setShow] = React.useState(false);
    return (
      <div className="relative">
        <Input
          ref={ref}
          // pr-9 leaves room for the toggle; `type` after {...props} so it always wins.
          className={cn("pr-9", className)}
          {...props}
          type={show ? "text" : "password"}
        />
        <button
          type="button"            // never submits the form
          tabIndex={-1}            // stay out of the tab order — Tab goes to the next field
          onClick={() => setShow((s) => !s)}
          aria-label={show ? "Hide password" : "Show password"}
          aria-pressed={show}
          className="absolute inset-y-0 right-0 flex items-center px-2.5 text-muted-foreground transition-colors hover:text-foreground"
        >
          {show ? <EyeOff size={16} /> : <Eye size={16} />}
        </button>
      </div>
    );
  }
);
PasswordInput.displayName = "PasswordInput";

export { PasswordInput };
